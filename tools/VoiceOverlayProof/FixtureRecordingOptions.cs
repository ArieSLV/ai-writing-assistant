namespace VoiceOverlayProof;

internal sealed record FixtureRecordingOptions(
    FixtureDefinition Fixture,
    int Take,
    string SessionId);
