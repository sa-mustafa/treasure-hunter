using Flurl.Http;
using HalconDotNet;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp.Formats;
using System.Buffers;
using System.Diagnostics;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

void ImageSearch()
{
    HTuple levels = new HTuple(3); levels.Append(1);
    HTuple subpixel = "none";
    HTuple min_score = 0.5;
    var model = new HShapeModel(@"model.shm");
    var list = Directory.GetFiles(@"C:\filtered");
    foreach (var file in list)
    {
        var input = new HImage(file);
        var blur = input.MeanImage(3, 3);
        model.FindScaledShapeModel(blur, 0, 0, 1, 2, min_score, 1, 0.5, subpixel, levels, 0.60, out HTuple row, out HTuple col, out HTuple angle, out HTuple scale, out HTuple score);
        if (score.Length > 0)
        {
            int rank = (int)(score.D * 100.0);
            Console.WriteLine($"\tFound code with score {score.D} - {file} !");
            if (score.D > 0.7)
                rank++;
        }
    }
}

async Task PageScroll()
{
    for (int i = 1; i <= 100; i++)
    {
        // https://www.digikala.com/search/category-stretching-tools
        // var url = $"https://api.digikala.com/v1/categories/stretching-tools/search/?sort=7&page={i}";
        var url = $"https://api.digikala.com/v1/categories/home-electric-accessories/search/?sort=4&page={i}";
        var pages = await url.GetStringAsync();
        var json = JObject.Parse(pages);
        foreach (var product in json["data"]["products"])
        {
            var id = product["id"].ToString();
            if (id == "7552020")
                break;
        }
    }
}

int counter = 0;
var pagesList = new Queue<string>();
var imageBytesQueue = new Queue<Tuple<string, byte[]>>();
var exitSignal = new ManualResetEvent(false);
int totalPages = 2000;
string output_dir = @"D:\results";

if (args.Length < 1)
{
    Console.WriteLine("Enter category as first argument.");
    Console.WriteLine();
    Console.WriteLine("For https://www.digikala.com/search/category-stretching-tools category is 'stretching-tools'");
    Console.WriteLine();
    return;
}

var category = args[0];
bool reverse = (args.Length == 2 && args[1] == "r");

var task = Task.Run(async () =>
{
    var timer = new Stopwatch();
    timer.Start();

    var list = new List<int>(100);
    if (reverse)
    {
        Console.WriteLine("Going in reverse order");
        for (int i = 100; i >= 1; i--)
            list.Add(i);
    }
    else
    {
        for (int i = 1; i <= 100; i++)
            list.Add(i);
    }

    int count = 0;
    foreach (var i in list)
    {
        // https://www.digikala.com/search/category-stretching-tools
        // var url = $"https://api.digikala.com/v1/categories/stretching-tools/search/?sort=7&page={i}";
        var url = $"https://api.digikala.com/v1/categories/{category}/search/?page={i}";
        var pages = await url.GetStringAsync();
        var json = JObject.Parse(pages);
        foreach (var product in json["data"]["products"])
        {
            var id = product["id"];
            var page = $"https://api.digikala.com/v2/product/{id}/";
            lock (pagesList) pagesList.Enqueue(page);
            count++;
        }
    }
    timer.Stop();
    Interlocked.Exchange(ref totalPages, count);
    Console.WriteLine($"Read all pages in {timer.Elapsed.TotalSeconds} seconds.");
});

var PageLoaders = new List<Thread>();
for (int i = 0; i < 20; i++)
{
    PageLoaders.Add(new Thread(PageLoaderThreadStart));
    PageLoaders[i].Start();
}

var ImageProcessors = new List<Thread>();
for (int i = 0; i < 10; i++)
{
    ImageProcessors.Add(new Thread(ImageProcessorsThreadStart));
    ImageProcessors[i].Start();
}

Console.WriteLine("Press 'q' to quit.");
// Keep the application running until the user presses 'q'
while (Console.ReadKey().Key != ConsoleKey.Q) ;
Console.WriteLine("uit exit signal received.");
exitSignal.Set();

void PageLoaderThreadStart()
{
    Thread.CurrentThread.Priority = ThreadPriority.Highest;
    while (!exitSignal.WaitOne(0))
    {
        if (pagesList.Count == 0)
        {
            Thread.Sleep(100);
            if (counter == totalPages)
            {
                Console.WriteLine($"All pages are fetched!");
                return;
            }
            continue;
        }

        string item;
        lock (pagesList)
        {
            if (!pagesList.TryDequeue(out item))
                continue;

            counter++;
            Console.WriteLine($"Processing product {counter} - {item}");
        }

        int retries = 0;
        JToken? imglist;
    retry:
        retries++;
        try
        {
            var productTask = item.GetStringAsync(); productTask.Wait();
            var product = productTask.Result;
            var jsonProduct = JObject.Parse(product);
            imglist = jsonProduct["data"]["product"]["images"]["list"];
        }
        catch
        {
            Console.WriteLine($"Product {counter} encountered 403 error! - {item}");
            Thread.Sleep(100);
            if (retries < 3)
                goto retry;
            else
                continue;
        }

        if (imglist == null)
        {
            Console.WriteLine($"Product {counter} list empty! - {item}");
            continue;
        }

        using var client = new HttpClient();
        foreach (var img in imglist)
        {
            try
            {
                var image = img["url"][0].ToString();
                var bytesTask = client.GetByteArrayAsync(image); bytesTask.Wait();
                byte[] bytes = bytesTask.Result;
                // Check for image size
                if (bytes.Length > 120 * 1024 || bytes.Length < 20 * 1024)
                    continue;

                lock (imageBytesQueue)
                    imageBytesQueue.Enqueue(Tuple.Create(item, bytes));
                //File.WriteAllBytes($@"C:\images\{Guid.NewGuid()}.jpg", bytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}

unsafe void ImageProcessorsThreadStart()
{
    Thread.CurrentThread.Priority = ThreadPriority.Normal;
    var opts = new DecoderOptions();
    opts.Configuration.PreferContiguousImageBuffers = true;

    HTuple levels = new HTuple(3); levels.Append(1);
    HTuple subpixel = "none";
    HTuple min_score = 0.6;
    var model = new HShapeModel(@"model.shm");

    while (!exitSignal.WaitOne(0))
    {
        if (imageBytesQueue.Count == 0)
        {
            Thread.Sleep(100);
            continue;
        }

        try
        {
            Tuple<string, byte[]> bytes;
            lock (imageBytesQueue)
                if (!imageBytesQueue.TryDequeue(out bytes))
                    continue;

            using var image = Image.Load<Rgba32>(opts, bytes.Item2);
            using Image<L8> gray = image.CloneAs<L8>();

            if (gray.DangerousTryGetSinglePixelMemory(out Memory<L8> memory))
            {
                using MemoryHandle handle = memory.Pin();
                var input = new HImage("byte", gray.Width, gray.Height, (IntPtr)handle.Pointer);
                var blur = input.MeanImage(3, 3);
                model.FindScaledShapeModel(blur, 0, 0, 1, 2, min_score, 1, 0.5, subpixel, levels, 0.60, out HTuple row, out HTuple col, out HTuple angle, out HTuple scale, out HTuple score);
                if (score.Length > 0)
                {
                    int rank = (int)(score.D * 100.0);
                    Console.WriteLine($"\tFound code with score {score.D} - {bytes.Item1} !");
                    input.WriteImage("jpeg", 0, @$"{output_dir}\{rank}-{Guid.NewGuid()}.jpg");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}