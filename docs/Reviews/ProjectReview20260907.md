# Review LlmTestServer — 2026-09-07

> Подальші виправлення та перевірки: [ReviewRemediation20260907.md](ReviewRemediation20260907.md). Нижче збережені початкові знахідки до виправлень.

## Висновок і межі перевірки

Перевірено поточний checkout `fa6f6ef` разом із незакоміченими змінами версії `0.1.1`. Знайдено **12 конкретних проблем: 4 P1 і 8 P2**. Одинадцять підтверджено окремим виконуваним harness, HTTP або браузером; ризик відновлення релізу встановлено аналізом workflow. Виправлення в production-код у цьому review не внесені.

Базовий повний прогін пройшов: **196/196**, без пропусків. Додатковий consumer поза репозиторієм встановив саме локальний `ManagedCode.LlmTestServer.Aspire.0.1.1.nupkg`, запустив власний AppHost і перевірив упакований сервіс. NuGet metadata підтверджує джерело `artifacts/packages`; source fallback для сервісу не використовувався. У браузері перевірено роботу Runtime settings цього consumer.

Переглянуті runtime і конфігурація, provider DTO/мапери/маршрути, API-contract/evidence тести, клієнти Microsoft.Extensions.AI, admin state/HTTP diagnostics, Aspire locator/пакування, sample service, CI/release та структура UI. Це не live conformance тест зовнішніх LLM і не повний responsive/accessibility аудит сайту. Попередні 90,07% line coverage є доказом виконаних рядків, але не закривають наведені нижче поведінкові прогалини.

Локальні докази: [harness та інструкції](../../artifacts/review-20260907/README.md), [структуровані спостереження](../../artifacts/review-20260907/observations.json), [baseline](../../artifacts/review-20260907/baseline-tests.log), [NuGet consumer](../../artifacts/review-20260907/package-consumer/run.log). Ці артефакти лежать у git-ignored `artifacts`; для перенесення звіту в інший checkout їх потрібно прикріпити окремо.

## P1 — першочергові виправлення

### R1. Dashboard із Aspire NuGet не отримує JavaScript

**Місце:** `src/ManagedCode.LlmTestServer.Aspire/ManagedCode.LlmTestServer.Aspire.csproj:13–16`.

Пакет бере сервіс із `bin/Release/net10.0` і виключає `*.staticwebassets.runtime.json`. Build output не містить фізичного `wwwroot`, на який покладаються static asset endpoints після видалення development manifest. У перевіреному nupkg взагалі **0 JavaScript-файлів**.

**Відтворення:** чистий consumer із PackageReference на `0.1.1`, `builder.AddLlmTestServer().WithApiKey("test-key")`, старт через Aspire.Hosting.Testing. `/health`, `/` та `/admin-api/models` повертають 200. Водночас `/_framework/blazor.web.js` і Fluent module повертають **200 / 0 bytes**. Service log прямо повідомляє, що застосунок працює не з publish output і static web assets не ввімкнені. У браузері натискання Runtime settings залишає popup закритим; сторінка містить script `_framework/blazor.web.js`.

**Наслідок:** користувач установлює пакет, бачить HTML dashboard, але не може скористатися інтерактивними контролами й авторизувати панель. Чинний Aspire integration test знаходить сервіс у source checkout через `LlmTestServerServiceLocator`, тому не перевіряє цю властивість nupkg.

**Виправлення:** пакувати service **publish output** зі статичними assets; додати NuGet-consumer тест поза solution, перевірку ненульового JS та браузерний сценарій відкриття settings/авторизації.

### R2. Tools і Structured Output заявлені, але не реалізовані

**Місця:** `src/ManagedCode.LlmTestServer.OpenAI/OpenAiCompatibility.cs:27–28,61–62`; `OpenAiChatModels.cs:7–23`; `src/ManagedCode.LlmTestServer/Scenarios/LlmTestServerScenarioResponse.cs`.

Профілі й документація прямо включають `Tools` та `StructuredOutput` у підтримувані можливості. Request DTO не має `tools`, `tool_choice` чи `response_format`; сценарій уміє повертати лише текст/чанки/помилку. Поля мовчки губляться під час десеріалізації.

**Відтворення:** chat completion із валідним function tool і `tool_choice="required"` повернув звичайний `"blue whale"`, `finish_reason="stop"`, без `tool_calls`. Запит із `response_format.type="json_schema"`, `strict=true` також повернув текст `blue whale`, який навіть не є JSON.

**Наслідок:** неможливо достовірно перевіряти agent tool loop чи typed-output parsing, хоча capability matrix стверджує підтримку. [Офіційний OpenAI контракт](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create) визначає обидві вимоги явно.

**Виправлення:** додати типізовані tool-call/result fixtures, відповідні request/response/stream mappings, перевірку або явне відхилення несумісної schema fixture. До реалізації прибрати непідтверджені capability claims. Детермінізм не потребує генерувати відповідь моделлю: потрібно відтворювати правильно налаштований протокол fixture.

### R3. Video lifecycle не має стану і ніколи не завершує OpenAI polling

**Місця:** `src/ManagedCode.LlmTestServer.OpenAI/OpenAiVideoModels.cs:33`; `OpenAiWireMapper.cs:361–373`; `src/ManagedCode.LlmTestServer.Hosting/LlmTestServerEndpointRouteBuilderExtensions.cs:2340–2375`.

`ToVideoResponse` не змінює початковий `Status="queued"`, хоча виставляє `completed_at`. GET генерує нову стандартну fixture і підставляє будь-який переданий ID. DELETE повертає `deleted=true`, не змінюючи стану.

**Відтворення:** `GET /openai/v1/videos/video_never_created` → 200/queued; DELETE того самого ID → 200/deleted=true; наступний GET → знову 200/queued. Жодного create для цього ID не було.

**Наслідок:** стандартний клієнт, що чекає completed/failed, продовжуватиме polling до власного timeout; помилки ID, видалення і відновлення job не тестуються. Аналогічне фабрикування результатів за ID є в Azure job/generation handlers, але нескінченний queued підтверджений саме для OpenAI.

**Виправлення:** окремий deterministic job store, налаштовані переходи станів, збережені параметри create, 404 для невідомих/видалених ID, reset/configure очищають jobs. Якщо fixture вже готова, послідовно повертати completed/100 замість queued.

### R4. Скасований chat може безповоротно спожити відповідь

**Місця:** `src/ManagedCode.LlmTestServer/Runtime/LlmTestServerRuntime.cs:142,293–306,1124–1127`.

Cancellation перевіряється тільки через `Task.Delay`. Без затримки вже скасований token не зупиняє запит. Rollback повертає позицію лише коли після reservation жоден інший запит не пересунув курсор.

**Відтворення A:** виклик із `new CancellationToken(true)` повернув `first`; наступний отримав `second`.

**Відтворення B:** запит A резервує `first` із затримкою, B отримує `second`, A скасовується; наступний запит повертає **409 / llm_test_server_scenario_exhausted**, хоча `first` ніхто не отримав.

**Наслідок:** паралельні integration tests і retry після cancellation залежать від порядку завершення запитів. Наявний тест перевіряє лише одиничне скасування delayed request.

**Виправлення:** перевіряти token до reservation і перед commit; обліковувати reservation/commit/rollback окремо для кожної відповіді або серіалізувати споживання відповідей одного сценарію.

## P2 — підтверджені функціональні прогалини

### R5. Однакові scenario ID у різних datasets ділять чергу

**Місця:** `src/ManagedCode.LlmTestServer/Runtime/LlmTestServerRuntime.cs:33,255,898–902`; `src/ManagedCode.LlmTestServer/Scenarios/LlmTestServerScenarioDatasetBuilder.cs:20–23`.

Dataset builder забезпечує унікальність лише всередині dataset, тоді як runtime ключує позиції одним `scenario.Id` після злиття всіх datasets.

**Доказ:** два datasets `dataset-a` і `dataset-b`, у кожному свій `greeting`, різні match-тексти й одна відповідь. Перший запит отримав `first`; перший запит другого dataset отримав 409/exhausted.

**Виправлення:** composite identity `(datasetId, scenarioId)` або явна валідація глобальної унікальності під час Build/Configure. Додати тест двох незалежних datasets з повторюваними локальними ID.

### R6. Невдалий configure стирає trace journal

**Місце:** `src/ManagedCode.LlmTestServer.Hosting/LlmTestServerEndpointRouteBuilderExtensions.cs:878–880`.

`traceStore.Reset()` виконується до валідації/snapshot усієї конфігурації у runtime. Наприклад, `{"models":null}` десеріалізується, а потім кидає `ArgumentNullException`.

**Доказ:** перед запитом журнал мав 3 записи, після помилки — 0; поточні models залишилися. TestServer віддає необроблений exception виклику; у звичайному HTTP host це серверна помилка.

**Виправлення:** повністю перевіряти нову конфігурацію до будь-якого очищення; застосовувати configuration/runtime journal/trace journal узгоджено; невалідний payload має давати 400 та зберігати старий стан.

### R7. Chat SSE не повертає запитаний usage

**Місця:** `src/ManagedCode.LlmTestServer.OpenAI/OpenAiChatModels.cs:7–23,92–108`; `src/ManagedCode.LlmTestServer.Hosting/LlmTestServerEndpointRouteBuilderExtensions.cs:3203–3209`.

Request DTO ігнорує `stream_options.include_usage`; chunk DTO не має `usage`. Потік завершується stop і [DONE] без фінальної usage події.

**Доказ:** HTTP-запит із `stream=true, stream_options={include_usage:true}` успішний, але жоден chunk не містить usage. В ordinary response для тієї самої fixture токени присутні.

**Наслідок:** SDK та телеметрія не отримують input/output/reasoning/cache counters у стандартному streaming сценарії. Це спільний writer для OpenAI-сумісних chat routes. [OpenAI reference](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create) описує фінальний usage chunk.

**Виправлення:** typed stream options, nullable usage на chunk, окремий завершальний usage chunk із порожніми choices при include_usage; перевірити official SDK usage events.

### R8. Власний IChatClient губить usage і фінальний stream update

**Місця:** `src/ManagedCode.LlmTestServer.Client/LlmTestServerChatClient.cs:42–49,104–114`; аналогічне відкидання usage є в `LlmTestServerEmbeddingGenerator.cs`.

Ordinary ChatResponse створюється без `Usage`, хоча HTTP DTO його має. Streaming повертає update лише за непорожнього text; фінальний chunk із finish reason та порожнім text відкидається.

**Доказ:** `answer.Usage == null`; для двох текстових чанків — **0 terminal updates, 0 UsageContent**. Ordinary result примусово отримує Stop замість відображення фактичного wire finish reason.

**Виправлення:** переносити `UsageDetails`, reasoning/cache details і фактичний finish reason; створювати metadata-only updates. Це окрема клієнтська зміна на додачу до R7: навіть із правильним server usage поточний клієнт його не переноситиме. [ChatResponse.Usage](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatresponse?view=net-11.0-pp) є стандартною частиною контракту Microsoft.Extensions.AI.

### R9. Валідний JSON із null у вкладених полях спричиняє exception

**Місце:** `src/ManagedCode.LlmTestServer.Hosting/LlmTestServerEndpointRouteBuilderExtensions.cs:4114–4119`.

Nullable runtime values можуть потрапити в non-nullable C# collection properties через JSON; initializer `=[]` не захищає від явного `null`. Gemini validation читає `content.Parts.Count` без перевірки null.

**Доказ:** `{"contents":[{"parts":null}]}` на generateContent викликає `NullReferenceException`. Контрольний `contents:[null]` правильно повертає 400, тож проблема саме в неповній nested validation. Подібні прямі `.Count` є в Messages/Contents інших native validators; їх потрібно перевірити матрицею, а не вважати всі вже відтвореними.

**Виправлення:** перевіряти null на кожному рівні DTO до mapper; додати negative matrix для відсутнього поля, null, порожнього масиву, null елемента і невірного типу. Повертати provider-native 400.

### R10. Legacy Azure api-version є лише metadata

**Місця:** `src/ManagedCode.LlmTestServer.AzureOpenAI/AzureOpenAiCompatibility.cs:78`; `src/ManagedCode.LlmTestServer.Hosting/LlmTestServerEndpointRouteBuilderExtensions.cs:1003–1018`.

Deployment chat передає запит у спільний handler без читання api-version. У Hosting немає перевірки цього query parameter.

**Доказ:** `/azure-openai/openai/deployments/gpt-4.1-mini/chat/completions?api-version=not-a-version` повернув 200 і спожив сценарій.

**Наслідок:** помилкова Azure SDK/config інтеграція проходить локальний LLM Test Server. [Офіційна legacy REST-специфікація](https://github.com/Azure/azure-rest-api-specs/blob/main/specification/cognitiveservices/data-plane/AzureOpenAI/inference/stable/2024-10-21/inference.json) вимагає api-version; нові `/openai/v1` маршрути мають інше правило і не повинні отримати dated-version вимогу.

**Виправлення:** явна політика версій окремо для legacy Azure, Foundry inference і preview video, з негативними тестами missing/unsupported version до runtime queue consumption.

### R11. Відхилений запит нараховує reasoning/output tokens

**Місце:** `src/ManagedCode.LlmTestServer/Runtime/LlmTestServerRuntime.cs:1545–1547`.

`CreateChatUsage` завжди додає модельний ReasoningTokens, у тому числі коли його викликають auth/unmatched/fault error branches без генерації.

**Доказ:** модель із reasoning=17, відсутній required token → 401, але `OutputTokens=17`, `ReasoningTokens=17`, `TotalTokens=19`; assertion summary теж накопичив 17 output tokens.

**Наслідок:** token accounting у тестах і dashboard показує генерацію, якої не було.

**Виправлення:** відокремити input diagnostics від успішної generation usage; для відмов до генерації output/reasoning=0, для змодельованого partial generation задавати usage явно.

### R12. Невдала публікація NuGet не відновлюється звичайним rerun

**Місце:** `.github/workflows/release.yml:46–48,85–99`.

GitHub Release створюється перед `dotnet nuget push`. Якщо push після цього падає або проходить частково, наступний запуск бачить існуючий GitHub Release і встановлює `should_release=false`, пропускаючи публікацію решти пакетів. Відсутній NuGet secret також дозволяє створити release без публікації пакетів.

**Тип доказу:** статичний аналіз порядку кроків і умов; публікацію, failure injection в GitHub чи роботу з реальним NuGet key не виконував. Наявний `workflow_dispatch(force=true)` дає ручний обхід, але звичайний rerun/наступний push не лікує неповний release.

**Виправлення:** використовувати draft release до завершення публікації або окремий marker повної доставки; перевіряти пакети незалежно від існування GitHub Release, повторювати ідемпотентний push із skip-duplicate. Якщо публікація обов'язкова, відсутній secret має завершувати workflow явною помилкою до створення final release.

## Що покращити в структурі та перевірках

1. **Перевіряти capability, а не лише існування route/test method.** `ProviderApiContractTests` звіряє metadata, множини маршрутів і наявність імен тестів. Звичайний chat test вважається evidence для route, який також заявляє tools/structured output. Потрібна матриця `(provider, operation, capability, transport, error behavior)` з незалежними assertions.
2. **Перевіряти реальний NuGet consumer у CI.** Зараз source fallback приховує R1. Після pack варто створювати consumer за межами repository tree, запускати його через Aspire.Hosting.Testing і перевіряти SDK call, assets і UI action. Окремо корисний Linux consumer, бо нинішнє локальне відтворення виконувалось на macOS arm64.
3. **Розділити великий Hosting файл.** `LlmTestServerEndpointRouteBuilderExtensions.cs` має 4602 рядки; runtime — 1760, AdminPage — 1538, AdminPanel — 1289. Винести provider route groups, validators і stream writers за відповідними межами відповідальності. Це полегшить окрему валідацію nullable JSON, API versions і usage.
4. **Закріпити 90% coverage gate у CI.** Поріг описаний у AGENTS і перевірявся локально, але чинні workflows запускають звичайні тести без coverage threshold. Додати той самий include/exclude scope і збереження звіту; не підміняти цим behavior evidence.
5. **Обмежити prompt cache для довгих load tests.** `_promptCacheEntries` не має capacity/eviction, хоча журнали обмежені. Це рекомендація для тривалих сесій з унікальними prompt keys, не виміряний у цьому review memory leak. Задати configurable capacity та deterministic eviction/reset policy.

## Рекомендований порядок

Спочатку R1 (працездатність встановленого продукту), R2/R3 (чесність API compatibility), R4/R5/R6 (ізоляція та збереження стану). Потім R7/R8/R11 як одна перевірка token usage від runtime до SDK; R9/R10 як negative protocol matrix; R12 — перед публікацією наступного release.

Після виправлень потрібні focused regressions для кожного сценарію, повний quality gate, оновлення capability evidence та повторний NuGet consumer/browser proof. Поточний зелений suite не є доказом відсутності цих дефектів.
