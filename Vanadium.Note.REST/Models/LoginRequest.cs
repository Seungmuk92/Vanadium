using System.ComponentModel.DataAnnotations;

namespace Vanadium.Note.REST.Models;

public record LoginRequest(
    [Required][MaxLength(100)] string Username,
    [Required][MaxLength(256)] string Password
);

public record SetupRequest(
    [Required][MaxLength(100)] string Username,
    [Required][MaxLength(256)] string Password
);
