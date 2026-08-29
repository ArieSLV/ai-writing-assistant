namespace VoiceOverlayProof;

internal sealed record FixtureDefinition(
    string Id,
    string TargetDuration,
    string Prompt);

internal static class FixtureCatalog
{
    private static readonly IReadOnlyDictionary<string, FixtureDefinition> Fixtures =
        new Dictionary<string, FixtureDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["F-01"] = new(
                "F-01",
                "15–20 секунд",
                "Завтра в 9:30 нужно подготовить черновик отчёта, проверить три графика и отправить Марине. Я фиксирую этот план заранее, чтобы утром выполнить все шаги по порядку и ничего не пропустить."),
            ["F-02"] = new(
                "F-02",
                "15–20 секунд",
                "Here is the plan for Friday. Deploy the staging build first. Then run twelve integration tests and make sure every result is recorded. When the checks are finished, notify Alex. Those are the required steps for the test run."),
            ["F-03"] = new(
                "F-03",
                "15–20 секунд",
                "Нужно обновить VoiceDictationController, проверить CancellationToken и открыть pull request. Названия VoiceDictationController и CancellationToken — это программные идентификаторы без пробелов. После обновления и проверки я открою pull request и кратко опишу изменения."),
            ["F-04"] = new(
                "F-04",
                "15–20 секунд",
                "Эм, встречу нужно, встречу нужно перенести на среду утром... Нет, я оговорился. Встречу нужно перенести на четверг после обеда. Пожалуйста, сообщите участникам только итоговый вариант."),
            ["F-05"] = new(
                "F-05",
                "15–20 секунд",
                "Я сначала хотел назначить встречу на вторник в два часа. Нет, это неверно: перенесите встречу на среду в три часа. Пожалуйста, используйте только исправленные день и время, а старый вариант больше не учитывайте."),
            ["F-06"] = new(
                "F-06",
                "15–20 секунд",
                "Нужно записать три последовательных пункта. Первый пункт — проверить логи и отметить найденные ошибки. Второй пункт — обновить тесты и запустить их ещё раз. Третий пункт — отправить краткое резюме с результатами проверки."),
            ["F-07"] = new(
                "F-07",
                "0.5–1 секунда",
                "Ничего не говорите. Остановите запись примерно через одну секунду."),
            ["F-08"] = new(
                "F-08",
                "около 30 секунд",
                "Нейтральный русский абзац на фоне умеренного бытового шума: подготовка тестовой версии, проверка настроек и просмотр результата на следующий день."),
            ["F-09"] = new(
                "F-09",
                "около 60 секунд",
                "Смешанный технический статус: цель, три выполненных шага, один blocker и следующий action с русскими и English identifiers.")
        };

    public static FixtureDefinition Get(string id)
    {
        if (!Fixtures.TryGetValue(id, out var fixture))
        {
            throw new ArgumentException("Fixture ID must be one of F-01..F-09.", nameof(id));
        }

        return fixture;
    }
}
