using MessagePack;

namespace SharedLib.Models;

[MessagePackObject]
/// <summary>
/// 聊天通知
/// </summary>
public class ChatNotify
{
    [Key(0)] public long UserId { get; set; }
    [Key(1)] public string Nickname { get; set; } = string.Empty;
    [Key(2)] public string Content { get; set; } = string.Empty;
}
