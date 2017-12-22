

using System;
using System.Collections.Generic;
using System.IO;

using TansakuKun.Data;
using TansakuKun.Native;
using TansakuKun.Elasticsearch;

namespace TansakuKun
{
  class Program
  {
    static void Main(string[] args)
    {
      // load config
      if (!File.Exists("config.yaml"))
      {
        Console.WriteLine("config file is not eists !");
        return;
      }
      var config = ConfigLoader.load("config.yaml");


      // enumerate all files
      var fileList = new Dictionary<ulong, FileNameAndParentFrn>();
      try
      {
        Console.WriteLine("READ START...");

        var enumerator = new FileEnumerator();
        fileList = enumerator.EnumerateVolume(config.target, new string[] { "*" });

        Console.WriteLine("...READ SUCCESS");
      }
      catch (Exception e)
      {
        Console.Error.WriteLine(e.Message);
        Console.WriteLine("...READ FAILURE");
        return;
      }

      Console.WriteLine("READ " + fileList.Count + " FILES");
      Console.WriteLine("");

      // insert elasticsearch
      try
      {
        Console.WriteLine("CLEANING...");
        Cleaner.deleteOlder(config.elasticsearch.host, config.elasticsearch.port);
        Console.WriteLine("...DONE");
        Console.WriteLine("");

        Console.WriteLine("WRITE START...");

        BulkInsert.execute(config.elasticsearch.host, config.elasticsearch.port, fileList);

        Console.WriteLine("...WRITE SUCCESS");
      }
      catch (Exception e)
      {
        Console.Error.WriteLine(e.Message);
        Console.WriteLine("...WRITE FAILURE");
        return;
      }

      // foreach (KeyValuePair<UInt64, FileNameAndParentFrn> entry in fileList)
      // {
      //   FileNameAndParentFrn file = (FileNameAndParentFrn)entry.Value;
      //   Console.WriteLine(file.Path);
      // }

      Console.WriteLine("");
      Console.WriteLine("");
      Console.WriteLine("DONE");
    }
  }
}
