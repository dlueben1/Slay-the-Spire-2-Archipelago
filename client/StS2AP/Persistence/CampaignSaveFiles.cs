using System.Security.Cryptography;

namespace StS2AP.Persistence;

/// <summary>Stores immutable save payloads so publishing new metadata cannot damage an older save.</summary>
internal static class CampaignSaveFiles
{
    public static string GetPath(string directory, string fileName, string hash)
    {
        bool validHash = hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
        if (!validHash || fileName != $"run-{hash}.save")
            throw new InvalidDataException("Campaign save filename is invalid.");
        return Path.Combine(directory, fileName);
    }

    public static string? Verify(string directory, string fileName, string hash)
    {
        string path = GetPath(directory, fileName, hash);
        if (!File.Exists(path))
            return "Save file is missing.";
        if (!string.Equals(hash, ComputeHash(File.ReadAllBytes(path)), StringComparison.OrdinalIgnoreCase))
            return "Save checksum does not match its metadata.";
        return null;
    }

    public static (string FileName, string Hash) Store(string directory, string source)
    {
        byte[] bytes = File.ReadAllBytes(source);
        string hash = ComputeHash(bytes);
        string fileName = $"run-{hash}.save";
        string destination = GetPath(directory, fileName, hash);
        Directory.CreateDirectory(directory);
        if (Verify(directory, fileName, hash) == null)
            return (fileName, hash);

        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        return (fileName, hash);
    }

    private static string ComputeHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
