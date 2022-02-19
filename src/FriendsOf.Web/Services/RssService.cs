using System.Net;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using FriendsOf.Web.Models;
using Microsoft.Azure.Cosmos;


namespace FriendsOf.Web.Services
{
    public class RssService
    {
        private const string MasterFile = "./wwwroot/master.xml";
        private const string FeedFile = "./wwwroot/feed.xml";
        private readonly IConfiguration _config;

        public RssService(IConfiguration config)
        {
            this._config = config;
        }

        public async Task DownloadFeeds()
        {
            var rss = new SyndicationFeed(_config["title"], _config["description"], null);

            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            var feedsConfig = _config.GetSection("feeds").GetChildren();

            foreach (var feedConfig in feedsConfig)
            {
                var feed = await DownloadFeed(feedConfig.Value);

                foreach (var item in feed.Items)
                {
                    //twitter handle
                    item.Authors.Add(new SyndicationPerson(feedConfig.Key));

                    //hack - also putting the authors Twitter handle in the copyright so that I can get to this field in a Logic App
                    item.Copyright = new TextSyndicationContent(feedConfig.Key);
                }
                rss.Items = rss.Items
                    .Union(feed.Items)
                    .GroupBy(i => i.Title.Text)
                    .Select(i => i.First())
                    .OrderByDescending(i => i.PublishDate);
            }

            await using var masterWriter = XmlWriter.Create(MasterFile);
            rss.SaveAsAtom10(masterWriter);
            masterWriter.Close();

            await using var feedWriter = XmlWriter.Create(FeedFile);
            rss.Items = rss.Items.Take(10);
            rss.SaveAsAtom10(feedWriter);
            feedWriter.Close();

            await InsertPosts(rss.Items);
        }

        private async Task InsertPosts(IEnumerable<SyndicationItem> rssItems)
        {
            var keyVaultUrl = _config["KeyVaultUrl"];
            var credential = new DefaultAzureCredential();
            var secretClient = new SecretClient(new Uri(keyVaultUrl), credential);
            var cosmosKey = await secretClient.GetSecretAsync("CosmosKey");

            var cosmosEndpoint = _config["CosmosEndpoint"];
            var cosmosDatabase = _config["CosmosDatabaseName"];
            var cosmosContainer = _config["CosmosContainerName"];

            var cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey.Value.Value);
            var container = cosmosClient.GetContainer(cosmosDatabase, cosmosContainer);

            foreach (var post in rssItems)
            {
                var postItem = PostModel.FromRssItem(post);

                await container.CreateItemAsync(postItem, new PartitionKey(postItem.PostId));
            }
        }

        private static async Task<SyndicationFeed> DownloadFeed(string url)
        {
            try
            {
                using var client = new HttpClient();
                var stream = await client.GetStreamAsync(url);
                return SyndicationFeed.Load(XmlReader.Create(stream));
            }
            catch (Exception)
            {
                return new SyndicationFeed();
            }
        }

        public IEnumerable<SyndicationItem> GetData(int page)
        {
            using var reader = XmlReader.Create(MasterFile);

            var count = int.Parse(_config["postsPerPage"]);
            var items = SyndicationFeed.Load(reader).Items.Skip((page - 1) * count).Take(count);
            return items.Select(item => { CleanItem(item); return item; });
        }

        private static void CleanItem(SyndicationItem item)
        {
            var summary = item.Summary != null ? item.Summary.Text : ((TextSyndicationContent)item.Content).Text;
            summary = Regex.Replace(summary, "<[^>]*>", ""); // Strips out HTML
            item.Summary = new TextSyndicationContent(string.Join("", summary.Take(300)) + "...");
        }
    }
}
