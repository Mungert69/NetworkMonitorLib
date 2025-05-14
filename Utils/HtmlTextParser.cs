using HtmlAgilityPack;
namespace NetworkMonitor.Utils;
public static class HtmlTextParser
{
    public static bool IsHtml(string text)
{
    // Quick, naive check:
    //   * looks for "<html>", "<body>", or "<head>"
    //   * ignoring case
    // You can expand with more robust checks if needed
    if (string.IsNullOrWhiteSpace(text)) return false;

    string lowered = text.ToLowerInvariant();
    return lowered.Contains("<html") || lowered.Contains("<body") || lowered.Contains("<head>");
}

    public static string ParseHtmlContent(string html)
    {
        // 1. Load the HTML
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // 2. Get the <body> or fallback to the entire document
        var bodyNode = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;

        // 3. Recursively build a string
        return GetTextWithNewlines(bodyNode).Trim();
    }

    private static string GetTextWithNewlines(HtmlNode node)
    {
        // Text node -> return the text content
        if (node.NodeType == HtmlNodeType.Text)
        {
            // Trim might help remove extra whitespace
            return node.InnerText.Trim();
        }

        // Element node -> decide prefix/suffix & recursion
        if (node.NodeType == HtmlNodeType.Element)
        {
            string prefix = string.Empty;
            string suffix = string.Empty;
            string nodeName = node.Name.ToLowerInvariant();

            // Example handling for some common tags
            switch (nodeName)
            {
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    // Markdown-style headings
                    int level = int.Parse(nodeName.Substring(1));
                    prefix = "\n" + new string('#', level) + " ";
                    suffix = "\n";
                    break;

                case "p":
                    prefix = "\n";
                    suffix = "\n";
                    break;

                case "br":
                    // If it's just a <br>, return a single newline
                    return "\n";

                case "li":
                    prefix = "- ";
                    suffix = "\n";
                    break;

                case "ul":
                case "ol":
                case "div":
                    prefix = "\n";
                    suffix = "\n";
                    break;

                case "a":
                    // Render as Markdown link: [text](href)
                    string href = node.GetAttributeValue("href", "#");
                    // The link text might be in child text nodes,
                    // so get the *inner text* from all children
                    string linkText = node.InnerText.Trim();
                    return $"[{linkText}]({href})";
            }

            // 4. Traverse child nodes
            var sb = new System.Text.StringBuilder();
            foreach (var child in node.ChildNodes)
            {
                string childText = GetTextWithNewlines(child);
                if (!string.IsNullOrWhiteSpace(childText))
                {
                    // Add a space if needed to separate concatenated text
                    if (sb.Length > 0 && !childText.StartsWith("\n") && !sb.ToString().EndsWith("\n"))
                    {
                        sb.Append(" ");
                    }
                    sb.Append(childText);
                }
            }

            // Combine prefix + child text + suffix
            string combined = sb.ToString().Trim();
            return prefix + combined + suffix;
        }

        // Otherwise (comment, etc.), skip
        return string.Empty;
    }
}
