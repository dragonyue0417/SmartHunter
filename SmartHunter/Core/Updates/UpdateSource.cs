using System;
using System.IO;
using System.Linq;

namespace SmartHunter.Core.Updates
{
    public sealed class UpdateSource
    {
        public const string DefaultUrl = "https://raw.githubusercontent.com/dragonyue0417/SmartHunter/unified/release/";
        public string Repository { get; private set; }
        public string Branch { get; private set; }
        public string Url => "https://raw.githubusercontent.com/" + Repository + "/" + Branch + "/release/";
        public static UpdateSource Parse(string value)
        {
            string text = (value ?? "").Trim();
            if (text.Contains("\\") || text.Contains("%") || text.Split('/').Any(p=>p=="." || p=="..") || !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "")
                throw new InvalidDataException("Invalid GitHub update URL.");
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            int first = uri.Host == "raw.githubusercontent.com" ? 2 : uri.Host == "github.com" && parts.Length > 2 && parts[2] == "tree" ? 3 : -1;
            if (first < 0 || parts.Length <= first + 1 || parts.Last() != "release") throw new InvalidDataException("Use a GitHub release folder URL.");
            string repository = String.Join("/", parts.Take(2)), branch = String.Join("/", parts.Skip(first).Take(parts.Length - first - 1));
            if (!UpdatePackage.ValidRepository(repository) || !UpdatePackage.ValidBranch(branch)) throw new InvalidDataException("Invalid GitHub update source.");
            return new UpdateSource { Repository = repository, Branch = branch };
        }
        public static bool TryParse(string value, out UpdateSource source)
        {
            try { source = Parse(value); return true; }
            catch (InvalidDataException) { source = null; return false; }
        }
    }
}
