namespace Vanadium.Note.REST.Models;

public record LoginRequest(string Username, string Password);
public record SetupRequest(string Username, string Password);
