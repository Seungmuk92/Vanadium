using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:7700", "https://localhost:7701")
              .AllowAnyMethod()
              .AllowAnyHeader());
});

builder.Services.AddDbContext<NoteDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddScoped<NoteService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<NoteDbContext>().Database.Migrate();

app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();
