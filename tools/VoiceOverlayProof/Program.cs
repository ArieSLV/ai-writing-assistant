namespace VoiceOverlayProof;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            return ProofSelfTest.Run();
        }

        FixtureRecordingOptions? fixtureOptions = null;
        if (args.Length > 0 && string.Equals(args[0], "--fixture", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 7 ||
                !string.Equals(args[2], "--take", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(args[3], out var take) ||
                !string.Equals(args[4], "--session", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(args[6], "--confirm-local-only", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    "Usage: VoiceOverlayProof --fixture F-01 --take 1 --session session-id --confirm-local-only");
                return 2;
            }

            fixtureOptions = new FixtureRecordingOptions(
                FixtureCatalog.Get(args[1]),
                take,
                args[5]);
        }

        using var context = new VoiceOverlayProofContext(fixtureOptions);
        Application.Run(context);
        return context.ExitCode;
    }
}
