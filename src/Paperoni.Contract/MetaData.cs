namespace Paperoni.Contract;

public class MetaData 
{
    public DateTime Date { get; set; }
    public List<string?> Caption { get; set; } = [];
    public int MessageId { get; set; }
    public long ChatId { get; set; }
    public int? ReplyMessageId { get; set; }
    public List<int> AlbumMessageIds { get; set; } = [];
}