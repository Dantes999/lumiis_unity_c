using System;
using System.Collections.Generic;

namespace Lumiis.Data;

public sealed class Account
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
    public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
    public int PasswordIterations { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public List<Character> Characters { get; set; } = new();
}
