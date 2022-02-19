using System.Security.Policy;
using System.ServiceModel.Syndication;
using Newtonsoft.Json;

namespace FriendsOf.Web.Models;

public class PostModel
{
    [JsonProperty(PropertyName = "id")]
    public string Id { get; } = Guid.NewGuid().ToString();

    [JsonProperty(PropertyName = "post_id")]
    public string? PostId { get; set; }

    [JsonProperty(PropertyName = "post_url")]
    public string? PostUrl { get; set; }

    [JsonProperty(PropertyName = "post_title")]
    public string? PostTitle { get; set; }

    [JsonProperty(PropertyName = "twitter_handle")]
    public string? TwitterHandle { get; set; }

    public static PostModel FromRssItem(SyndicationItem rssItem)
    {
        return new PostModel
        {
            PostId = rssItem.Id,
            PostUrl = rssItem.Links[0].Uri.ToString(),
            PostTitle = rssItem.Title.Text,
            TwitterHandle = rssItem.Authors[0].Email
        };
    }
}