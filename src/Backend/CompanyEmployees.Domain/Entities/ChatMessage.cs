namespace CompanyEmployees.Domain.Entities;

// One chat message between two people. There is deliberately no Conversation entity: a 1:1
// conversation *is* the pair of users, so the thread is every ChatMessage whose sender/recipient
// match that pair in either direction. Nothing to keep in sync, nothing to create before the
// first message can be sent.
public class ChatMessage
{
    public Guid Id { get; set; }

    public Guid SenderId { get; set; }
    public User Sender { get; set; } = null!;

    public Guid RecipientId { get; set; }
    public User Recipient { get; set; } = null!;

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Null until the recipient opens the thread. Stored so the inbox can count unread; not
    // shown to the sender, so this is not a read receipt.
    public DateTime? ReadAt { get; set; }
}
