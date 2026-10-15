using Xunit;

namespace StS2AP.RegressionTests;

internal sealed class ArtifactFactAttribute : FactAttribute
{
    public ArtifactFactAttribute(string environmentVariable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentVariable)))
            Skip = $"Set {environmentVariable} to run this packaging check.";
    }
}
