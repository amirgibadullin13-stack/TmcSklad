using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
namespace Tmc;

public class Category { public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = ""; }
public class Item { public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = ""; public Guid CategoryId { get; set; } public string Unit { get; set; } = "шт"; }
public class Receipt { public Guid Id { get; set; } = Guid.NewGuid(); public DateTime Date { get; set; } = DateTime.Today; public Guid ItemId { get; set; } public decimal Qty { get; set; } public string Note { get; set; } = ""; public string User { get; set; } = ""; }
public class IssueLine { public Guid ItemId { get; set; } public decimal Qty { get; set; } }
public class Issue { public Guid Id { get; set; } = Guid.NewGuid(); public int Number { get; set; } public DateTime Date { get; set; } = DateTime.Now; public string Recipient { get; set; } = ""; public string Note { get; set; } = ""; public string User { get; set; } = ""; public List<IssueLine> Lines { get; set; } = new(); }
public class User { public Guid Id { get; set; } = Guid.NewGuid(); public string Login { get; set; } = ""; public string FullName { get; set; } = ""; public string Salt { get; set; } = ""; public string PwHash { get; set; } = ""; public string Password { get; set; } = ""; public bool IsAdmin { get; set; } public List<Guid> CategoryIds { get; set; } = new(); }
public class AppData
{
    public List<Category> Categories { get; set; } = new();
    public List<Item> Items { get; set; } = new();
    public List<Receipt> Receipts { get; set; } = new();
    public List<Issue> Issues { get; set; } = new();
    public List<User> Users { get; set; } = new();
    public int NextAct { get; set; } = 1;
}

public static class Store
{
    public static string Dir = Path.Combine(AppContext.BaseDirectory, "data");
    public static string DbPath => Path.Combine(Dir, "db.json");
    public static string BackupDir => Path.Combine(Dir, "backups");
    public static AppData D = new();
    public static User? Cur;
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void Load()
    {
        Directory.CreateDirectory(Dir);
        if (File.Exists(DbPath))
        {
            AutoBackup();
            D = JsonSerializer.Deserialize<AppData>(File.ReadAllText(DbPath), Opt) ?? new();
        }
        if (D.Users.Count == 0) D.Users.Add(NewUser("admin", "Администратор", "admin", true));
        Save();
    }

    public static AppData? Parse(string path)
    {
        try { return JsonSerializer.Deserialize<AppData>(File.ReadAllText(path), Opt); } catch { return null; }
    }

    public static void Save()
    {
        var tmp = DbPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(D, Opt));
        File.Move(tmp, DbPath, true);
    }

    public static void AutoBackup()
    {
        Directory.CreateDirectory(BackupDir);
        if (File.Exists(DbPath)) File.Copy(DbPath, Path.Combine(BackupDir, $"db_{DateTime.Now:yyyyMMdd_HHmmss}.json"), true);
        foreach (var f in Directory.GetFiles(BackupDir, "db_*.json").OrderByDescending(x => x).Skip(30)) File.Delete(f);
    }

    public static string Pw(string salt, string pass) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + pass)));

    public static User NewUser(string login, string name, string pass, bool admin)
    {
        var s = Guid.NewGuid().ToString("N");
        return new User { Login = login, FullName = name, Salt = s, PwHash = Pw(s, pass), Password = pass, IsAdmin = admin };
    }
}

public static class H
{
    public static AppData D => Store.D;
    public static string Cat(Guid id) => D.Categories.FirstOrDefault(c => c.Id == id)?.Name ?? "?";
    public static Item? Find(Guid id) => D.Items.FirstOrDefault(i => i.Id == id);
    public static string Label(Item i) => $"{Cat(i.CategoryId)} / {i.Name}";
    public static decimal Rcv(Guid id) => D.Receipts.Where(r => r.ItemId == id).Sum(r => r.Qty);
    public static decimal Iss(Guid id) => D.Issues.SelectMany(a => a.Lines).Where(l => l.ItemId == id).Sum(l => l.Qty);
    public static decimal Stock(Guid id) => Rcv(id) - Iss(id);
    public static string N(decimal d) => d.ToString("0.##", CultureInfo.CurrentCulture);
    public static bool TryQty(string s, out decimal q) => decimal.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out q) && q > 0;
    public static bool TryDate(string s, out DateTime d) => DateTime.TryParse(s, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out d);
    public static void Err(string m) => MessageBox.Show(m, "Ошибка");
    public static bool Yes(string m) => MessageBox.Show(m, "Подтверждение", MessageBoxButton.YesNo) == MessageBoxResult.Yes;

    public static void OpenAct(Issue a)
    {
        string p;
        try { p = ActDoc.Create(a); }
        catch (Exception ex) { Err("Не удалось создать акт: " + ex.Message); return; }
        try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); }
        catch { MessageBox.Show("Акт сохранён:\n" + p, "Акт выдачи"); }
    }
}
