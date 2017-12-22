
namespace TansakuKun.Data
{

  public class ConfigObj
  {
    public string target { get; set; }

    public Es elasticsearch { get; set; }

    public class Es
    {
      public string host { get; set; }
      public int port { get; set; }

      public int chunk { get; set; }
    }
  }
}