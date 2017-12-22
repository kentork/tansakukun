using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elasticsearch.Net;
using TansakuKun.Data;
using TansakuKun.Data.Json;

namespace TansakuKun.Elasticsearch
{
  public class BulkInsert
  {


    public static void execute(string host, int port, Dictionary<ulong, FileNameAndParentFrn> files, int chunkSize = 10000)
    {
      var settings = new ConnectionConfiguration(new Uri("http://" + host + ":" + port))
                          .RequestTimeout(TimeSpan.FromMinutes(2));
      var lowlevelClient = new ElasticLowLevelClient(settings);

      var index = DateTime.Now.ToString("yyyyMMddHHmmss");
      var type = "pathes";

      long id = 1;
      foreach (var chank in files.Values.Chunks(chunkSize))
      {
        var json = new List<object>();
        foreach (var file in chank)
        {
          json.Add(new Index(id, index, type));
          json.Add(new FileEntry(file.Path, ""));

          id++;
        }

        var indexResponse = lowlevelClient.Bulk<StreamResponse>(PostData.MultiJson(json));
        using (var responseStream = indexResponse.Body)
        {
          Console.Write(".");
        }
      }
      Console.WriteLine("");
    }
  }

  // from https://webbibouroku.com/Blog/Article/chunk-linq
  public static class Extensions
  {
    public static IEnumerable<IEnumerable<T>> Chunks<T>(this IEnumerable<T> list, int size)
    {
      while (list.Any())
      {
        yield return list.Take(size);
        list = list.Skip(size);
      }
    }
  }
}