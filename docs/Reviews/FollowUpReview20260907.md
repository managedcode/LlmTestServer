# Додатковий review після виправлень — 2026-09-07

**Оновлення 2026-09-08:** усі шість знахідок і три запропоновані покращення реалізовані. Початкові відтворення нижче збережені як історія; актуальні докази наведені в розділі «Виправлення».

Перевірено поточний незакомічений checkout після попередніх 12 виправлень. Знайдено шість додаткових поведінкових прогалин рівня P2; кожна відтворена окремим deterministic runtime/HTTP harness. Це нові крайові випадки, а не висновок із відсотка coverage. Production-код у цьому проході не змінювався.

## Підтверджені знахідки

### F1 — P2: отриманий snapshot дозволяє змінити внутрішній журнал

Місця: `src/ManagedCode.LlmTestServer/Runtime/LlmTestServerRuntime.cs:920`, `:1450–1457`; `Scenarios/LlmTestServerMessage.cs:9`.

`GetAssertionSummary()` копіює лише список подій. Вкладений `Messages[i].ToolCalls` залишається тим самим mutable `List`, який зберігається в журналі. У harness після `summary.Events[0].Messages[0].ToolCalls.Clear()` та додавання `Id="injected"` наступний незалежний `GetAssertionSummary()` повернув **injected** замість original.

Це порушує задокументований immutable snapshot та дозволяє одному in-process споживачу змінити докази іншого тесту. Зміни довжин payload також оминають облік retention budget. Потрібні глибокі snapshots або незмінні вкладені колекції на read boundary; окремий тест має мутувати отриманий результат, а не лише вхідну конфігурацію.

### F2 — P2: `parallel_tool_calls=false` мовчки ігнорується

Місця: `src/ManagedCode.LlmTestServer.OpenAI/OpenAiChatModels.cs:7–18`, `OpenAiToolModels.cs:44–57`.

Для fixture з двома викликами weather запит із `parallel_tool_calls:false` отримав **200 та два tool_calls**. Поля немає в wire DTO і neutral request. Поточні тести кількох викликів перевіряють їх перенесення, але не обмеження запиту.

[OpenAI Chat reference](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create) визначає цей перемикач для parallel function calling. Варто перенести обмеження в neutral runtime та відхиляти несумісну fixture без її споживання. Аналогічні provider-specific перемикачі потребують власної матриці, а не припущення про спільну семантику.

### F3 — P2: адаптер Gemini не повністю перетворює OpenAPI Schema

Місце: `src/ManagedCode.LlmTestServer.Gemini/GeminiWireMapper.cs:98–130`.

`NormalizeSchemaNode` лише переводить назви типів у lowercase, після чого OpenAPI Schema оцінюється як JSON Schema Draft 2020-12. Два коректні wire contracts відтворили помилку:

- Fixture `{"city":null}` та responseSchema з обов'язковим `city: {type:"STRING", nullable:true}` → **409 / ABORTED**.
- Fixture `["Paris"]` та `type:"ARRAY", items:{type:"STRING"}, minItems:"1"` → **409**, повідомлення `'minItems' value must be a number, found String`.

[Gemini Schema reference](https://ai.google.dev/api/generate-content?hl=en#Schema) описує `nullable` і рядкове представлення int64 для minItems/maxItems та інших лімітів. Потрібен адаптер цих полів, який обходить саме schema nodes, із позитивними й негативними тестами nullable, числових меж, anyOf і вкладених схем. `responseJsonSchema` має зберегти власну семантику.

### F4 — P2: fixture mismatch невидимий у runtime assertion summary

Місце: `src/ManagedCode.LlmTestServer/Runtime/LlmTestServerRuntime.cs:312–316`.

Для текстової fixture `not json` і `RequireJson=true` runtime повернув **409 / llm_tck_fixture_mismatch**, але summary містив **TotalEvents=0, ErrorsReturned=0, Events=[]**. Early return обходить AddEvent та облік usage.

HTTP trace може показати помилку, але користувачі runtime/control assertions не отримують відповідного діагностичного запису. Треба додати окремий вид події/лічильник mismatch або послідовно використовувати error event. Черга відповідей при цьому повинна залишатися незмінною. Це слід перевірити одночасним assertion на 409, journal та наступну відповідь.

### F5 — P2: HTTP chat без обов'язкової моделі проходить успішно

Місце: `src/ManagedCode.LlmTestServer.OpenAI/OpenAiChatModels.cs:18`.

Запит `{"messages":[{"role":"user","content":"hello"}]}` на `/openai/v1/chat/completions` повернув **200**, підставив gpt-4.1-mini та спожив fixture. Default model у wire DTO приховує відсутнє поле ще до ValidateChatRequest.

[OpenAI contract](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create) вимагає model. Зручний default доречний у builder/client, але HTTP input повинен дозволяти відрізнити omitted від configured. Для Azure deployment route модель і далі має надходити з URL; потрібен окремий позитивний тест цього винятку.

### F6 — P2: Exact matching не може вимагати відсутність tool calls

Місце: `src/ManagedCode.LlmTestServer/Runtime/LlmTestServerRuntime.cs:1298–1304`.

`WithExactMatch` для порожнього assistant message без tool calls зіставився з assistant message, що містить **додатковий weather tool call**, і повернув 200 / `matched empty assistant`. Порожній expected.ToolCalls завжди працює як wildcard; null ToolCallId має аналогічну поведінку.

Це дозволяє зайвому кроку agent loop пройти точний сценарій. Потрібно явно розділити семантику «не перевіряти поле» та «вимагати порожнє поле»: наприклад, strict Exact або окремі match constraints. Зміну треба врахувати як уточнення публічного matching contract та перевірити позитивним і негативним прикладом; Contains може зберігати поточні wildcard правила.

## Що покращити після цих виправлень

1. **Прив'язати evidence до capability і режиму.** `ProviderApiContractTests:238–303` перевіряє operation, streaming flag, існування test method та згадку в inventory. Він не доводить, що конкретна capability має positive/negative cases. Варто використовувати `(provider, operation, capability, transport, outcome)` та конкретні case IDs. Критерій успіху: видалення тесту заборонених parallel calls або nullable schema ламає відповідний guard.
2. **Перенести tool wire validation до provider packages.** `LlmTestServerToolRequestValidation` централізує поля OpenAI, Anthropic, Gemini, Cohere, Ollama і Bedrock через `typeof(T)`. Це дублює знання mapper-ів у Hosting. Provider-owned validation має повертати нейтральний validation result, а Hosting — лише оформляти HTTP відповідь.
3. **Обмежити video store за кількістю та байтами.** `LlmTestServerRuntime.Videos.cs:5–33` зберігає всі jobs і копії bytes до delete/reset. Це не виміряний memory leak, але явна відсутність capacity на відміну від журналу й prompt cache. Додати configurable MaxVideoJobs/MaxVideoBytes та детерміновану політику переповнення; перевірити на великих fixtures без time-based sleeps.

Рекомендований порядок: F1 та F4 для надійності доказів; F2, F3, F5 для protocol conformance; F6 як уточнення matching contract. Потім посилення capability evidence, щоб нові крайові випадки не знову проходили повз перевірки.

## Докази та межі

Локальний harness: [Program.cs](../../artifacts/review-followup-20260907/Program.cs), [observations.json](../../artifacts/review-followup-20260907/observations.json), [run.log](../../artifacts/review-followup-20260907/run.log).

Відтворення з кореня checkout:

```sh
dotnet run --project artifacts/review-followup-20260907/Harness.csproj --configuration Release
```

Harness посилається на реальні project references і використовує ASP.NET TestServer; він не викликає зовнішні LLM. Він виводить спостереження поточної поведінки, а не є passing acceptance suite. Артефакти git-ignored; для передачі review в інший checkout їх потрібно прикріпити або перенести репродукції в тести.

- status: complete — review завершений, виправлення не виконані.
- plan: виконані inspection, official-doc comparison, runtime/HTTP reproductions та пріоритезація.
- actions_taken: доданий цей review і локальний harness; production-код збережений.
- validation_skills: mcaf-code-review; referenced skill files відсутні в інсталяції, використано SKILL.md і repo contracts. run-tests/coverage не повторювалися для документаційного review.
- verification: сім executable observations підтверджують шість findings; git diff --check успішний. Попередні 321 tests / 90,32% coverage є попереднім результатом, не новим прогоном.
- remaining: F1–F6 та три окремі рекомендації вище. Нової перевірки GitHub CI, публікації або повного browser/responsive аудиту в цьому проході немає.


## Виправлення — 2026-09-08

| Пункт | Реалізовано | Регресійний доказ |
| --- | --- | --- |
| F1 | Глибокий snapshot повідомлень/tool calls/chunks як у summary, так і в TryGetEvent | FollowUpRuntimeTests.SummaryMutations_CannotChangeRetainedMessagesOrChunksAsync |
| F2 | Neutral AllowParallelToolCalls; OpenAI Chat/Responses, Anthropic disable_parallel_tool_use, MEAI AllowMultipleToolCalls | FollowUpEndpointTests.ParallelTools_RespectRequestLimitAndPreserveQueueAsync; ProviderValidationBoundaryTests.DisabledParallelTools_AllowsZeroOrOneCallAsync; ToolFixtureClientTests.ExtensionsAi_RespectsSingleToolOptionWithoutConsumingFixtureAsync |
| F3 | Окремий GeminiSchemaMapper: nullable, усі шість int64 limits, properties/items/anyOf; незмінні example/default, окремий responseJsonSchema | FollowUpSchemaTests; FollowUpEndpointTests.GeminiSchema_ValidatesNullableNestedAlternativesAndStringLimitsAsync |
| F4 | ErrorReturned з request/scenario correlation, error text і usage; черга не споживається | FollowUpRuntimeTests.FixtureMismatch_IsCountedAndJournaledWithoutConsumingResponseAsync; provider capability matrix |
| F5 | Wire model не має прихованого default; Azure deployment бере модель із URL, клієнт зберігає зручний default | FollowUpEndpointTests.ChatModel_IsRequiredExceptForAzureDeploymentRoutesAsync |
| F6 | Exact порівнює null/empty tool metadata; Contains зберігає wildcard-поведінку | FollowUpRuntimeTests.ExactMatch_RejectsUnexpectedToolMetadataWhileContainsKeepsWildcardsAsync |
| Evidence | Реальні accept/reject запити для всіх заявлених tools/schema операцій 13 провайдерів у JSON/stream режимах; guard перевіряє executable tests та конкретні edge-case IDs | ProviderCapabilityEvidenceTests |
| Validation | Валідація tool wire fields перенесена з Hosting до шести provider packages; нейтральний result і спільні JSON primitives | ProviderValidationBoundaryTests; ToolFixtureValidationTests; наявні legacy-field regressions |
| Video storage | MaxVideoJobs/MaxVideoBytes, 256 jobs/64 MiB defaults, явний 409 overflow, release capacity при delete/reset/configure, захист колізій IDs | FollowUpRuntimeTests; FollowUpEndpointTests.VideoCapacity_ReturnsConflictThroughProviderEndpointAsync |

Оптимізація Gemini mapper прибирає повторний DeepClone кожного вкладеного піддерева: mapper працює з одним власним parsed tree. Це структурне скорочення копіювання, не виміряна за benchmark гарантія швидкості.

Перевірки поточного checkout:

- `dotnet format ManagedCode.LlmTestServer.slnx --verify-no-changes --no-restore` — пройдено.
- Release build — пройдено; два наявні попередження ASPIRE010 про AspireUseCliBundle=false, помилок немає.
- Повний набір, включно з provider contract/evidence, Azure SDK, Bedrock SDK та Aspire tests: **379/379**, повторено під coverage.
- `bash scripts/coverage.sh`: Coverlet total line **90.58%**, поріг 90% пройдено. ReportGenerator HTML: `artifacts/coverage/report/summary.html`.
- Усі **17 NuGet packages 0.1.1** зібрано в `artifacts/packages`.
- `git diff --check` — пройдено.

Логи: `artifacts/review-followup-20260907/{format-check,build,full-tests,coverage,pack}.log`. Усі нові регресії додані у звичайні вихідні файли під `tests`; початковий exploratory harness лишається git-ignored і більше не потрібний для відтворення виправлень.

- status: improved
- plan: усі дев'ять пунктів реалізовано та перевірено.
- actions_taken: runtime, wire DTOs/mappers, provider-owned validators, bounded store, capability matrix, regression tests, docs and MTP command examples.
- validation_skills: run-tests/tunit, coverlet/reportgenerator, mcaf-code-review. Review reference files відсутні; використано AGENTS.md та початковий review.
- verification: format, Release build, 379 tests, 90.58% line coverage, 17 packages.
- remaining: none у межах цього review; commit/push не виконувалися.


Фінальний NuGet consumer: **1/1 passed** у свіжій директорії поза репозиторієм з окремим package cache. SHA-512 встановленого Aspire package збігається з локальним `.nupkg`. Реальний Aspire AppHost підняв сервіс із package payload; перевірено health/control API, статичні Blazor/Fluent assets, schema-validated tool call та інтерактивний dashboard у Chromium. Лог: `artifacts/review-followup-20260907/package-consumer.log`; screenshots: `artifacts/package-consumer/`. Локальні перевірки завершені; CI/release/publish не запускалися.
