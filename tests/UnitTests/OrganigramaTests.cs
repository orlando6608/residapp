using ResidApp.Domain.Organization;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>ADM-07 (0073): reglas de los cargos del organigrama, el árbol y la plantilla de CJ.</summary>
public class OrganigramaTests
{
    private sealed record Node(Guid Id, Guid? Superior, string Name);

    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();

    [Fact]
    public void Cargo_SeRecortaYLaNotaVaciaEsSinNota()
    {
        var superior = Guid.NewGuid();

        var data = Position.Validate("  Dirección médica ", onePerson: true, superior, "   ");

        Assert.Equal(new PositionData("Dirección médica", true, superior, null), data);
        Assert.Equal("Coordina con Medicina", Position.ValidateNote("  Coordina con Medicina "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Cargo_SinNombre_EsInvalido(string? name) =>
        Assert.Equal(Position.InvalidCode, Assert.Throws<DomainValidationException>(() => Position.ValidateName(name)).Message);

    [Fact]
    public void Cargo_ConNombreONotaDemasiadoLargos_EsInvalido()
    {
        Assert.Throws<DomainValidationException>(() => Position.ValidateName(new string('a', Position.MaxNameLength + 1)));
        Assert.Throws<DomainValidationException>(() => Position.ValidateNote(new string('a', Position.MaxNoteLength + 1)));
        Assert.Equal(Position.MaxNameLength, Position.ValidateName(new string('a', Position.MaxNameLength)).Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void PersonaSinCuenta_SinNombre_EsInvalida(string? name) =>
        Assert.Equal("POSITION_OCCUPANT_INVALID", Assert.Throws<DomainValidationException>(() => Position.ValidatePersonName(name)).Message);

    [Fact]
    public void PersonaSinCuenta_SoloSeGuardaElNombre_ConUnLimite()
    {
        Assert.Equal("Rosa Gil", Position.ValidatePersonName("  Rosa Gil "));
        Assert.Throws<DomainValidationException>(() => Position.ValidatePersonName(new string('a', Position.MaxPersonNameLength + 1)));
    }

    [Fact]
    public void Ciclo_ElCargoNoPuedeDependerDeSiMismoNiDeUnSubordinado()
    {
        // A <- B <- C (C depende de B, B de A) y D aparte.
        var superiors = new Dictionary<Guid, Guid?> { [A] = null, [B] = A, [C] = B, [D] = null };

        Assert.True(PositionTree.WouldCreateCycle(superiors, A, A));
        Assert.True(PositionTree.WouldCreateCycle(superiors, A, B));
        Assert.True(PositionTree.WouldCreateCycle(superiors, A, C));
        Assert.True(PositionTree.WouldCreateCycle(superiors, B, C));
        Assert.False(PositionTree.WouldCreateCycle(superiors, C, A));
        Assert.False(PositionTree.WouldCreateCycle(superiors, A, D));
        Assert.False(PositionTree.WouldCreateCycle(superiors, A, null));
    }

    [Fact]
    public void Arbol_AnidaPorSuperior_OrdenaPorNombre_YUnSuperiorAusenteEsRaiz()
    {
        var missing = Guid.NewGuid();
        var nodes = new[]
        {
            new Node(C, B, "Enfermería"), new Node(B, A, "Coordinación"), new Node(A, null, "Dirección"),
            new Node(D, B, "Auxiliares"), new Node(Guid.NewGuid(), missing, "Huérfano"),
        };

        var roots = PositionTree.Nest(nodes, n => n.Id, n => n.Superior, n => n.Name);

        Assert.Equal(["Dirección", "Huérfano"], roots.Select(r => r.Item.Name));
        var coordination = Assert.Single(roots[0].Children);
        Assert.Equal("Coordinación", coordination.Item.Name);
        Assert.Equal(["Auxiliares", "Enfermería"], coordination.Children.Select(c => c.Item.Name));
    }

    [Fact]
    public void Arbol_ConUnCicloNoSeCuelgaNiPierdeCargos()
    {
        var nodes = new[] { new Node(A, B, "Uno"), new Node(B, A, "Dos") };

        var roots = PositionTree.Nest(nodes, n => n.Id, n => n.Superior, n => n.Name);

        // Ninguno es raíz (cada uno depende del otro): no se pinta nada, pero termina.
        Assert.Empty(roots);
    }

    [Fact]
    public void Plantilla_SonLosOchoCargosDeCJ_Coherentes()
    {
        var entries = PositionTemplate.Entries;

        Assert.Equal(8, entries.Count);
        Assert.Equal(entries.Count, entries.Select(e => e.Key).Distinct().Count());
        Assert.Equal(entries.Count, entries.Select(e => e.Name.ToLowerInvariant()).Distinct().Count());
        Assert.Single(entries, e => e.SuperiorKey is null);
        Assert.All(entries, e => Assert.Equal(e.Name, Position.ValidateName(e.Name)));
        Assert.All(entries.Where(e => e.Note is not null), e => Assert.Equal(e.Note, Position.ValidateNote(e.Note)));
        // Cada superior existe y va antes que quien depende de él (se insertan en este orden).
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].SuperiorKey is { } key)
            {
                Assert.InRange(entries.ToList().FindIndex(e => e.Key == key), 0, i - 1);
            }
        }

        Assert.Equivalent(new[] { "Dirección del centro", "Dirección médica / Coordinación médica", "Coordinación / Supervisión de Enfermería",
            "Responsable de Administración" }, entries.Where(e => e.OnePerson).Select(e => e.Name).ToArray());
    }
}
