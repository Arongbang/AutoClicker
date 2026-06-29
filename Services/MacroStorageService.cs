using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoClicker.Models;

namespace AutoClicker.Services;

public class MacroStorageService
{
  private static readonly string Dir  = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoClicker");
  private static readonly string File = Path.Combine(Dir, "macros.json");

  private static readonly JsonSerializerOptions Opts = new()
  {
    WriteIndented = true,
    Converters    = { new JsonStringEnumConverter() }
  };

  public async Task SaveAsync(IEnumerable<Macro> macros)
  {
    Directory.CreateDirectory(Dir);
    await System.IO.File.WriteAllTextAsync(File,
      JsonSerializer.Serialize(macros.ToList(), Opts));
  }

  public async Task<List<Macro>> LoadAsync()
  {
    if (!System.IO.File.Exists(File)) return new List<Macro>();
    try
    {
      var json = await System.IO.File.ReadAllTextAsync(File);
      return JsonSerializer.Deserialize<List<Macro>>(json, Opts) ?? new List<Macro>();
    }
    catch
    {
      return new List<Macro>();
    }
  }
}
