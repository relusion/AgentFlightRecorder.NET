namespace AzureOpenAISample;

internal static class ConsoleHelper
{
    public static string Truncate(string text, int maxLength = 120) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
