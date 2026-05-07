using System.Text;
using System.Text.RegularExpressions;

// Resolve --password and --update-env from args
string? password = null;
string? envPath = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--password" && i + 1 < args.Length)
        password = args[++i];
    else if (args[i] == "--update-env" && i + 1 < args.Length)
        envPath = args[++i];
}

// Prompt interactively if --password not supplied
if (password is null)
{
    Console.Write("New password: ");
    var sb = new StringBuilder();
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
            sb.Remove(sb.Length - 1, 1);
        else if (key.Key != ConsoleKey.Backspace)
            sb.Append(key.KeyChar);
    }
    Console.WriteLine();
    password = sb.ToString();
}

// Validate complexity
var errors = new List<string>();
if (password.Length < 12)
    errors.Add("at least 12 characters");
if (!password.Any(char.IsUpper))
    errors.Add("at least one uppercase letter");
if (!password.Any(char.IsLower))
    errors.Add("at least one lowercase letter");
if (!password.Any(char.IsDigit))
    errors.Add("at least one digit");
if (!password.Any(c => "!@#$%^&*_-+=".Contains(c)))
    errors.Add("at least one special character from: !@#$%^&*_-+=");

if (errors.Count > 0)
{
    Console.Error.WriteLine("Password does not meet complexity requirements:");
    foreach (var e in errors)
        Console.Error.WriteLine($"  - {e}");
    return 1;
}

// Generate BCrypt hash (cost 12)
var hash = BCrypt.Net.BCrypt.HashPassword(password, 12);
var line = $"AUTH_PASSWORD_HASH={hash}";

// If --update-env not supplied, default to .env in the current working directory (repo root)
if (envPath is null)
{
    envPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
}

if (!File.Exists(envPath))
{
    Console.WriteLine($"Warning: .env not found at {envPath}");
    Console.WriteLine();
    Console.WriteLine(line);
    Console.WriteLine();
    Console.WriteLine("Copy the line above into your .env file.");
    return 0;
}

var envLines = File.ReadAllLines(envPath, Encoding.UTF8);
var replaced = false;
for (int i = 0; i < envLines.Length; i++)
{
    if (Regex.IsMatch(envLines[i], @"^AUTH_PASSWORD_HASH\s*="))
    {
        envLines[i] = line;
        replaced = true;
        break;
    }
}

if (!replaced)
{
    var list = new List<string>(envLines) { line };
    envLines = [.. list];
}

File.WriteAllLines(envPath, envLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine($"Updated AUTH_PASSWORD_HASH in {envPath}");
Console.WriteLine("Restart the API container to apply the change:");
Console.WriteLine("  docker compose restart api");
return 0;
