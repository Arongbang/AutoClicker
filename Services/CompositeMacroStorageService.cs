using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoClicker.Models;

namespace AutoClicker.Services;

public class CompositeMacroStorageService
{
  private static readonly string Dir  = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoClicker");
  private static readonly string File = Path.Combine(Dir, "composites.json");

  private static readonly JsonSerializerOptions Opts = new()
  {
    WriteIndented = true,
    Converters    = { new JsonStringEnumConverter() }
  };

  public async Task SaveAsync(IEnumerable<CompositeMacro> composites)
  {
    Directory.CreateDirectory(Dir);
    await System.IO.File.WriteAllTextAsync(File,
      JsonSerializer.Serialize(composites.ToList(), Opts)).ConfigureAwait(false);
  }

  public async Task<List<CompositeMacro>> LoadAsync()
  {
    if (!System.IO.File.Exists(File)) return new List<CompositeMacro>();
    try
    {
      var json = await System.IO.File.ReadAllTextAsync(File).ConfigureAwait(false);
      return JsonSerializer.Deserialize<List<CompositeMacro>>(json, Opts) ?? new List<CompositeMacro>();
    }
    catch
    {
      return new List<CompositeMacro>();
    }
  }
}
