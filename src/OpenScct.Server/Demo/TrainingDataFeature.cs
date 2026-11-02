using System;

namespace OpenScct.Server;

/// <summary>Opt-in capture and download of demo training companions. Ordinary demo recording is independent.</summary>
public static class TrainingDataFeature
{
    public const string EnvironmentVariable = "SVM_ENABLE_TRAINING_DATA";

    /// <summary>Off unless explicitly enabled before the game/server settings are created.</summary>
    public static bool EnabledByEnvironment => Environment.GetEnvironmentVariable(EnvironmentVariable) is { } value &&
        (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
