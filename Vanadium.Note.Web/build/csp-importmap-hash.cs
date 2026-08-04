// Bakes the CSP hash of the SDK-generated import map into nginx.conf.
//
// index.html carries an EMPTY <script type="importmap"></script> that the .NET
// SDK fills at publish time with the Blazor framework fingerprint map
// (./_framework/dotnet.js -> ./_framework/dotnet.<hash>.js + the SRI table).
// That element is an INLINE script, so the site CSP -- which deliberately omits
// 'unsafe-inline' from script-src as the second line of defense against stored
// note XSS (issue #199) -- would block it, the dotnet.js mapping would be lost
// and the app would hang on the loading spinner.
//
// A nonce is not an option for a static file server, and the map's content
// changes with every fingerprint, so the hash is computed here from the actual
// published index.html and substituted into the CSP template. Both the global
// and the /share/ policy carry the placeholder.
//
// Run as a file-based app (no project file), from a directory that contains no
// .csproj, or `dotnet run` resolves the project instead of this file:
//     dotnet run csp-importmap-hash.cs -- <index.html> <nginx.conf.in> <out>

using System.Security.Cryptography;
using System.Text;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: csp-importmap-hash <index.html> <nginx.conf.in> <nginx.conf.out>");
    return 2;
}

var (indexPath, templatePath, outputPath) = (args[0], args[1], args[2]);
const string Placeholder = "__IMPORTMAP_SHA256__";
var open = Encoding.UTF8.GetBytes("<script type=\"importmap\">");
var close = Encoding.UTF8.GetBytes("</script>");

var html = File.ReadAllBytes(indexPath);
var start = IndexOf(html, open, 0);
if (start < 0)
{
    Console.Error.WriteLine($"error: no <script type=\"importmap\"> element in {indexPath}.");
    return 1;
}

var contentStart = start + open.Length;
var end = IndexOf(html, close, contentStart);
if (end < 0)
{
    Console.Error.WriteLine($"error: unterminated <script type=\"importmap\"> element in {indexPath}.");
    return 1;
}

// CSP hashes cover the element's text content byte for byte -- no trimming.
var content = html[contentStart..end];
if (content.Length == 0)
{
    Console.Error.WriteLine(
        $"error: the import map in {indexPath} is empty -- the SDK did not inject the framework map. " +
        "Check that wwwroot/index.html still has an EMPTY <script type=\"importmap\"></script> and that " +
        "OverrideHtmlAssetPlaceholders is enabled.");
    return 1;
}

var hash = $"sha256-{Convert.ToBase64String(SHA256.HashData(content))}";

var template = File.ReadAllText(templatePath);
if (!template.Contains(Placeholder, StringComparison.Ordinal))
{
    Console.Error.WriteLine($"error: {templatePath} has no {Placeholder} placeholder.");
    return 1;
}

File.WriteAllText(outputPath, template.Replace(Placeholder, hash, StringComparison.Ordinal));
Console.WriteLine($"import map CSP hash: '{hash}' -> {outputPath}");
return 0;

static int IndexOf(byte[] haystack, byte[] needle, int from)
{
    for (var i = from; i <= haystack.Length - needle.Length; i++)
    {
        var match = true;
        for (var j = 0; j < needle.Length; j++)
        {
            if (haystack[i + j] != needle[j]) { match = false; break; }
        }

        if (match) return i;
    }

    return -1;
}
