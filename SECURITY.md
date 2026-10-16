# Security / Безопасность

KeyFish reads your keystrokes to fix them, so security matters. See the **Privacy** section of the [README](README.md) for exactly what it does with your input.

## Reporting a vulnerability

Please **don't open a public issue**. Use GitHub's private reporting instead: the **Security** tab of this repository → **Report a vulnerability**. You'll get an answer as soon as possible, and the fix will be credited in the changelog if you'd like.

## How releases are protected

- Every release is built by GitHub Actions from the source code in this repository; the workflow is in `.github/workflows/release.yml`.
- Each release publishes `KeyFish.exe.sha256`. The one-click update downloads only from this repository's releases and refuses a file whose checksum or version doesn't match.
- Third-party actions in the workflow are pinned to exact commits.

---

## Сообщить об уязвимости

Пожалуйста, **не создавайте публичное обсуждение (issue)**. Воспользуйтесь приватной формой GitHub: вкладка **Security** этого репозитория → **Report a vulnerability**. Ответ будет как можно скорее, а исправление по желанию будет отмечено в списке изменений.

## Как защищены релизы

- Каждый релиз собирает GitHub Actions из исходного кода этого репозитория; сценарий сборки — в `.github/workflows/release.yml`.
- В каждом релизе публикуется `KeyFish.exe.sha256`. Обновление в один клик скачивает файлы только из релизов этого репозитория и отказывается ставить файл, если не совпадает контрольная сумма или версия.
- Сторонние компоненты сборки закреплены на точных коммитах.
