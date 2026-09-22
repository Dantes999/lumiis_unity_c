namespace Lumiis.Auth;

/// <summary>Marker-Typ für WebApplicationFactory&lt;T&gt; in Tests. Der implizite Top-Level-Statements-
/// Program-Typ (global::Program, siehe Program.cs) kollidiert dort mit Lumiis.Server.Program: beide heißen
/// im globalen Namensraum schlicht "Program" und sind beide für Lumiis.Tests sichtbar (Lumiis.Auth.Program
/// ist bewusst public gemacht, Lumiis.Server.Program über dessen [InternalsVisibleTo("Lumiis.Tests")]) -
/// nicht per Namensraum qualifizierbar, da top-level statements immer in den globalen Namensraum kompilieren.
/// WebApplicationFactory braucht nur irgendeinen öffentlichen Typ aus der Zielassembly (findet den
/// Einstiegspunkt selbst über dessen Assembly), nicht zwingend den Entry-Point-Typ selbst.</summary>
public sealed class AuthEntryPointMarker
{
}
