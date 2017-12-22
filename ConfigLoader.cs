using System.IO;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using TansakuKun.Data;

namespace TansakuKun
{
  class ConfigLoader
  {
    public static ConfigObj load(string path)
    {
      ConfigObj config = null;
      using (var input = new StreamReader(path, Encoding.UTF8))
      {
        var deserializer = new DeserializerBuilder().Build();
        config = deserializer.Deserialize<ConfigObj>(input);
      }
      return config;
    }
  }
}