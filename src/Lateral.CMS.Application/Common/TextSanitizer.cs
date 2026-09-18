namespace Lateral.CMS.Application.Common;

public static class TextSanitizer
{
    /// <summary>
    /// Makes untrusted text safe to store and log: removes control characters (log forging, NUL bytes
    /// that several stores reject outright) and truncates to <paramref name="maxLength"/>.
    /// </summary>
    public static string? ForStorage(string? value, int maxLength)
    {
        if (value is null)
            return null;

        var cleaned = new string([.. value.Where(c => !char.IsControl(c))]).Trim();

        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }
}
