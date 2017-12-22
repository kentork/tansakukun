
namespace TansakuKun.Data.Json
{

  public class FileEntry
  {
    public string path { get; set; }
    public string date { get; set; }

    public FileEntry(string path, string date)
    {
      this.path = path;
      this.date = date;
    }
  }
}