using System.Net;

namespace Car_Rental_System_BE.Infrastructure.Services.Registration;

public static class OtpEmailTemplate
{
    public static string Render(string name, string otp)
    {
        var safeName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? "bạn" : name.Trim());
        var safeOtp = WebUtility.HtmlEncode(otp);
        return $$"""
            <!doctype html>
            <html lang="vi">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Mã xác thực Thong Dong</title></head>
            <body style="margin:0;padding:0;background-color:#FBFBF9;color:#0F172A;font-family:'Be Vietnam Pro',Arial,sans-serif;-webkit-text-size-adjust:100%;">
              <div style="display:none;max-height:0;overflow:hidden;opacity:0;">Mã xác thực cho tài khoản Thong Dong của bạn. Vui lòng không chia sẻ mã này.</div>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#FBFBF9;">
                <tr><td align="center" style="padding:32px 16px;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background-color:#FFFFFF;border:1px solid #E2E4E8;border-radius:20px;overflow:hidden;">
                    <tr><td style="padding:28px 28px 24px;border-bottom:1px solid #E2E4E8;">
                      <p style="margin:0;color:#B45309;font-size:24px;font-weight:800;letter-spacing:-0.5px;">Thong Dong<span style="color:#0F172A;">.</span></p>
                      <p style="margin:6px 0 0;color:#64748B;font-size:12px;line-height:1.6;">Đồng hành trên mọi hành trình</p>
                    </td></tr>
                    <tr><td style="padding:28px;">
                      <h1 style="margin:0 0 16px;font-size:24px;line-height:1.4;letter-spacing:-0.5px;">Xác thực tài khoản của bạn</h1>
                      <p style="margin:0 0 12px;color:#334155;font-size:14px;line-height:1.8;">Xin chào {{safeName}},</p>
                      <p style="margin:0 0 24px;color:#334155;font-size:14px;line-height:1.8;">Nhập mã dưới đây vào màn hình xác thực trên Thong Dong để tiếp tục yêu cầu của bạn.</p>
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:20px 12px;background-color:#FEF3C7;border:1px solid #FDE68A;border-radius:12px;">
                        <p style="margin:0 0 10px;color:#92400E;font-size:12px;font-weight:600;">MÃ XÁC THỰC OTP</p>
                        <p style="margin:0;color:#0F172A;font-family:'Geist Mono',Consolas,monospace;font-size:32px;font-weight:700;letter-spacing:6px;line-height:1.4;">{{safeOtp}}</p>
                      </td></tr></table>
                      <p style="margin:20px 0 0;color:#334155;font-size:14px;line-height:1.8;">Mã chỉ sử dụng một lần và hết hạn sau vài phút. Không chia sẻ mã này với bất kỳ ai.</p>
                      <p style="margin:12px 0 0;color:#64748B;font-size:12px;line-height:1.8;">Nếu bạn không thực hiện yêu cầu này, hãy bỏ qua email.</p>
                    </td></tr>
                    <tr><td style="padding:20px 28px;background-color:#F2F2EE;border-top:1px solid #E2E4E8;">
                      <p style="margin:0;color:#64748B;font-size:12px;line-height:1.8;">Thong Dong · Hệ thống thuê xe<br>Email được gửi tự động, vui lòng không trả lời.</p>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body></html>
            """;
    }
}
