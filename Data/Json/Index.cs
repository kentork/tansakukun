
namespace TansakuKun.Data.Json
{

  public class Index
  {
    public Metadata index { get; set; }

    public Index(long id, string _index, string type)
    {
      index = new Metadata(_index, type, id.ToString());
    }

    public class Metadata
    {
      public Metadata(string index, string type, string id)
      {
        _index = index;
        _type = type;
        _id = id;
      }
      public string _index { get; set; }
      public string _type { get; set; }
      public string _id { get; set; }
    }
  }
}