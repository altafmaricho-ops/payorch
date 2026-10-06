namespace PayOrch.Infrastructure.Psp.Razorpay;

public class RazorpayOptions
{
    public const string SectionName = "Razorpay";

    public string KeyId { get; set; } = default!;
    public string KeySecret { get; set; } = default!;
    public string WebhookSecret { get; set; } = default!;
    public string BaseUrl { get; set; } = "https://api.razorpay.com/v1";

    /// <summary>Minutes before a created order is treated as expired if unpaid.</summary>
    public int OrderExpiryMinutes { get; set; } = 15;
}
