namespace BetaGiaiPhong.Web.Models.ViewModels;

public sealed class ErrorViewModel
{
    public string? RequestId { get; set; }
    public string? CorrelationId { get; set; }
    public int StatusCode { get; set; } = 500;
    public string Message { get; set; } = "Đã xảy ra lỗi trong quá trình xử lý yêu cầu.";

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    public bool ShowCorrelationId => !string.IsNullOrEmpty(CorrelationId);
}
