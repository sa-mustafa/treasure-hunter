using Flurl.Http;
using HalconDotNet;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp.Formats;
using System.Buffers;
using System.Diagnostics;
using System.Reflection;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

void ImageSearch()
{
    HTuple levels = new HTuple(3); levels.Append(1);
    HTuple subpixel = "none";
    HTuple min_score = 0.45;
    var model = new HShapeModel(@"model.shm");
    var list = Directory.GetFiles(@"C:\filtered");
    foreach (var file in list)
    {
        var input = new HImage(file);
        var blur = input.MeanImage(1, 1);
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
int imageSaveIndex = 0;
string output_dir = @"C:\results{0}";
for (int dir_index = 1; ; dir_index++)
{
    string cur_dir = string.Format(output_dir, dir_index);
    if (!Directory.Exists(cur_dir))
    {
        Directory.CreateDirectory(cur_dir);
        output_dir = cur_dir;
        Console.WriteLine($"Saving probable results to {cur_dir}");
        break;
    }
}

if (args.Length < 3)
{
    Console.WriteLine("ganj.exe category min_price max_price [r]");
    Console.WriteLine();
    Console.WriteLine("For https://www.digikala.com/search/category-stretching-tools category is 'stretching-tools'");
    //Console.WriteLine("For https://www.digikala.com/search/category-stretching-tools full_url is https://api.digikala.com/v1/categories/stretching-tools");
    Console.WriteLine();
    return;
}

var category = args[0];
var min_price = args[1];
var max_price = args[2];
Console.WriteLine($"Searching between {min_price} {max_price}");

bool reverse = (args.Length == 4 && args[3] == "r");

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
        //var url = $"https://api.digikala.com/v1/categories/{category}/search/?sort=7&page={i}";
        var url = $"https://api.digikala.com/v1/categories/{category}/search/?has_selling_stock=1&price[max]={max_price}&price[min]={min_price}&sort=20&page={i}";
        //var url = $"{api_url}/?has_selling_stock=1&device_id=e08cca83-8897-458b-855b-93d80f3e2855&price[min]={min_price}&price[max]={max_price}&sort=20&page={i}";
        var pages = await url.GetStringAsync();
        var json = JObject.Parse(pages);
        foreach (var product in json["data"]["products"])
        {
            var id = product["id"];
            var page = $"https://api.digikala.com/v2/product/{id}/";
            //if (id.ToString() == "2722676")
            lock (pagesList) pagesList.Enqueue(id.ToString());
            count++;
        }
        Console.WriteLine($"Added page {count} to queue.");

    }
    timer.Stop();
    Interlocked.Exchange(ref totalPages, count);
    Console.WriteLine("########################################################");
    Console.WriteLine($"Read all {count} pages in {timer.Elapsed.TotalSeconds} seconds.");
    Console.WriteLine("########################################################");
});

var PageLoaders = new List<Thread>();
for (int i = 0; i < 20; i++)
{
    PageLoaders.Add(new Thread(PageLoaderThreadStart));
    PageLoaders[i].Start();
}

var ImageProcessors = new List<Thread>();
for (int i = 0; i < 16; i++)
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
            //Console.WriteLine($"Processing product {counter} - {item}");
        }

        int retries = 0;
        JToken? imglist;
    retry:
        retries++;
        try
        {
            var page = $"https://api.digikala.com/v2/product/{item}/";
            var productTask = page.GetStringAsync(); productTask.Wait();
            var product = productTask.Result;
            var jsonProduct = JObject.Parse(product);
            imglist = jsonProduct["data"]["product"]["images"]["list"];
        }
        catch
        {
            Console.WriteLine($"Product {counter} encountered 403 error! - {item}, trying again...");
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
                if (bytes.Length > 150 * 1024 || bytes.Length < 20 * 1024)
                    continue;

                lock (imageBytesQueue)
                    imageBytesQueue.Enqueue(Tuple.Create(item, bytes));
                //File.WriteAllBytes($@"C:\images\{item}={Guid.NewGuid()}.jpg", bytes);
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
    HTuple min_score = 0.40;
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

            using var image = Image.Load<Rgb24>(opts, bytes.Item2);
            using Image<L8> gray = image.CloneAs<L8>();

            if (gray.DangerousTryGetSinglePixelMemory(out Memory<L8> memory))
            {
                using MemoryHandle handle = memory.Pin();
                var input = new HImage("byte", gray.Width, gray.Height, (IntPtr)handle.Pointer);
                var blur = input.MeanImage(1, 1);
                model.FindScaledShapeModel(blur, 0, 0, 1, 2, min_score, 1, 0.5, subpixel, levels, 0.60, out HTuple row, out HTuple col, out HTuple angle, out HTuple scale, out HTuple score);
                if (score.Length > 0)
                {
                    int rank = (int)(score.D * 100.0);
                    //Console.WriteLine($"\tFound code with score {score.D} - {bytes.Item1} !");
                    int saveIndex = Interlocked.Increment(ref imageSaveIndex);
                    Task.Run(() => 
                    {
                        input.WriteImage("jpeg", 0, @$"{output_dir}\{saveIndex:0000}-{bytes.Item1}-{rank}-{Guid.NewGuid().ToString()[..8]}.jpg");
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}