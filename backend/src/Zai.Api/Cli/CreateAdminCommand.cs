using System.Text;
using Microsoft.EntityFrameworkCore;
using Zai.Api.Auth;
using Zai.Api.Data;
using Zai.Api.Domain;

namespace Zai.Api.Cli;

public static class CreateAdminCommand
{
    public const string Name = "create-admin";
    private const string PasswordVariable = "ZAI_ADMIN_PASSWORD";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var username = ReadOption(args, "--username") ?? "admin";
        var password = Environment.GetEnvironmentVariable(PasswordVariable) ?? Prompt($"Hasło dla „{username}”: ");
        if (password.Length < 8)
        {
            Console.Error.WriteLine("Hasło musi mieć co najmniej 8 znaków.");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var admin = await db.Admins.FirstOrDefaultAsync(a => a.Username == username);
        if (admin is null)
        {
            db.Admins.Add(new Admin { Username = username, PasswordHash = hasher.Hash(password), CreatedAt = now });
            Console.WriteLine($"Utworzono administratora „{username}”.");
        }
        else
        {
            admin.PasswordHash = hasher.Hash(password);
            admin.PasswordChangedAt = now;
            Console.WriteLine($"Zmieniono hasło administratora „{username}”.");
        }

        await db.SaveChangesAsync();
        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string Prompt(string label)
    {
        Console.Write(label);
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0) password.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
    }
}
