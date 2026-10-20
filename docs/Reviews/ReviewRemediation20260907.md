# Виправлення review — 2026-09-07

У поточному checkout виправлено 12 зауважень із [початкового review](ProjectReview20260907.md). Попереднє оновлення до 0.1.1 та залежностей збережене. Коміт, push і публікація не виконувалися.

## Зміни

| Review | Результат | Регресійний доказ |
| --- | --- | --- |
| R1 | Aspire пакує portable **publish output** сервісу разом із JavaScript/CSS, а не build output. Runtime settings недоступна до підключення інтерактивного Blazor. | NuGet-only consumer поза репозиторієм: власний AppHost, HTTP assets, Chromium відкриває settings і авторизує dashboard. |
| R2 | Реалізовані typed tool fixtures, повний tool-result loop, provider-native streaming і JSON Schema validation. Claims повернені лише для реалізованих операцій. | `ToolFixture*Tests`: provider matrix, official SDKs, MEAI automatic invocation, mismatch recovery, state isolation. |
| R3 | OpenAI/Azure jobs зберігають параметри й унікальні ID; готові OpenAI fixtures мають completed/100. Retrieve/content/delete перевіряють існування, reset/configure очищають jobs. | `ReviewVideoRegressionTests`, оновлений OpenAI lifecycle test, чинні Azure preview tests. |
| R4 | Перевірка cancellation до резервування/commit; rollback повертає конкретну відповідь навіть після завершення іншого паралельного запиту. | Pre-cancel та concurrent cancellation тести перевіряють відсутність втрати/повтору відповідей. |
| R5 | Резервування ключується окремим snapshot сценарію, а не лише локальним ID. | Два datasets з однаковим scenario ID незалежно працюють до й після reset. |
| R6 | Snapshot конфігурації валідується до мутацій; невалідний payload повертає 400, зберігаючи runtime і traces. | Матриця null-полів configuration; перевірка trace IDs і наступної відповіді старої черги. |
| R7 | `stream_options.include_usage` керує фінальним usage chunk із порожніми choices; збережений provider-specific cache shape. | HTTP SSE opt-in/out і офіційний OpenAI SDK. |
| R8 | MEAI chat/embeddings переносять usage; streaming зберігає terminal та metadata-only updates. | Перевірка text, finish reason, reasoning/input/output/total та embedding usage. |
| R9 | Nested null collections/items проходять валідацію до native mapper. | Gemini, Anthropic, Ollama, Cohere та OpenAI negative HTTP matrix; посилена Bedrock валідація. |
| R10 | Legacy Azure deployment routes приймають підтримувані 2024-10-21 GA та 2025-04-01-preview, які автоматично обирає SDK, і відхиляють невідомі версії (уточнено 2026-09-09); Foundry inference — 2024-05-01-preview; video preview — preview. Нові v1 routes не вимагають dated query. | Invalid/missing/duplicate query tests; офіційний Azure SDK без явно вибраної версії; чинні Azure/Foundry v1 SDK тести. |
| R11 | Відмови до генерації та scripted errors не створюють output/reasoning usage. | Auth, unmatched, scripted error й успішна reasoning-відповідь у runtime. |
| R12 | NuGet публікується до final GitHub Release. Лише marker завершеної доставки дозволяє пропустити наступний запуск. Відсутній key дає помилку до публікації. | `scripts/release.sh` ставить marker після public-feed gates; `scripts/verify-release.py` перевіряє реальні GitHub/NuGet artifacts. Fake CLI tests вилучено; end-to-end publication qualification очікує coordinated release. |

R2 тепер має реалізацію, а не лише виправлення capability claims. Межі підтримки (зокрема Bedrock Converse та відсутність function tools у Perplexity) явно описані в [контракті fixtures](../Features/ToolAndStructuredFixtures.md).

## Додаткові покращення

- Prompt cache обмежений 4096 prefixes за замовчуванням; `WithPromptCacheCapacity` задає capacity, zero вимикає кеш; FIFO eviction детермінований.
- Provider validators, streaming writers і video handlers винесені з основного Hosting файла в окремі файли. Основний файл суттєво зменшений; tool-request validation також має окремий файл.
- CI і release застосовують production coverage threshold 90%, запускають зовнішній NuGet consumer і перевірку release recovery, зберігають verification artifacts.
- Source-string тест пакування замінено реальною перевіркою вмісту й поведінки встановленого пакета.

## Перевірка остаточного diff

- Format check (`dotnet format ... --verify-no-changes --no-restore`) та Release build — успішно. Залишаються два наявні ASPIRE010 повідомлення про вимкнений CLI bundle.
- Повний suite, включно з provider contract/evidence та Aspire integration: **321 passed, 0 failed, 0 skipped**. У цьому доопрацюванні додано 73 виконувані test cases порівняно з попередніми 248.
- Coverlet: **90,32% production line coverage** (9214/10201), поріг 90% пройдений. [HTML report](../../artifacts/coverage/report/summary.html).
- Усі **17 NuGet packages 0.1.1** зібрані повторно. Aspire — **13.5.3**.
- Зовнішній NuGet-only consumer із власним package cache й AppHost: **1 passed**. SHA-512 встановленого Aspire пакета звірено з локальним nupkg. Перевірено assets, schema-validated tool call через запакований сервіс, відкриття settings і авторизацію dashboard у Chromium. [Dashboard після авторизації](../../artifacts/package-consumer/package-dashboard-authorized.png).
- `python3 scripts/verify-release.py` — успішно: partial publish, rerun, completion marker, force та missing key.
- `git diff --check` — успішно.

Логи остаточного прогону: `artifacts/fixes-20260907/tools-format-check.log`, `tools-build.log`, `tools-coverage.log`, `tools-pack.log`, `tools-package-consumer.log`. Артефакти git-ignored; CI налаштований зберігати coverage/consumer evidence.

Усі 12 конкретних зауважень початкового review закриті в перевіреному локальному diff. Це macOS arm64 verification; GitHub CI, Linux consumer і реальна публікація NuGet у цій сесії не запускалися. Коміт і push не виконані.
