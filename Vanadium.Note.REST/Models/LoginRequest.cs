using System.ComponentModel.DataAnnotations;

namespace Vanadium.Note.REST.Models;

public record LoginRequest(
    [Required][MaxLength(256)] string Password
);
