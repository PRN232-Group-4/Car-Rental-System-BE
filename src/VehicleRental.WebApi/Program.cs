using Car_Rental_System_BE.Infrastructure;
using Car_Rental_System_BE.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddWebApiLogging();

// Thứ tự đăng ký là một phần của hợp đồng: lần đăng ký cuối cho một kiểu service sẽ thắng và
// IEnumerable<T> trả về theo thứ tự đăng ký. AddWebApi đăng ký cây options và MVC trước, sau đó là
// infrastructure, rồi các service application, và cuối cùng là phần xác thực/giới hạn tần suất vì các
// sự kiện JWT của nó dùng các service application đã đăng ký ở trên.
builder.Services.AddWebApi(builder.Configuration);
// Infrastructure sở hữu nguồn dữ liệu Npgsql, kết nối Redis, các hosted service và mọi đăng ký
// repository/adapter. Program.cs chỉ ghép chúng lại.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddWebApiSecurity(builder.Configuration);

var app = builder.Build();

app.UseWebApi();

app.Run();

/// <summary>
/// Mở kiểu entry-point ngầm định cho <c>WebApplicationFactory&lt;Program&gt;</c> để smoke test cấu hình có
/// thể khởi động đúng host này. Kiểu này không có thành viên nào.
/// </summary>
public partial class Program { }
