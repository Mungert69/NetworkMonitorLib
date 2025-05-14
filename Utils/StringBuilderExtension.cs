using System.Text;

namespace NetworkMonitor.Utils;
public static class StringBuilderExtensions
{
    public static StringBuilder TrimEnd(this StringBuilder sb, char[] trimChars)
    {
        int end = sb.Length - 1;
        while (end >= 0 && trimChars.Contains(sb[end]))
        {
            end--;
        }

        if (end < sb.Length - 1)
        {
            sb.Length = end + 1;
        }

        return sb;
    }
}
