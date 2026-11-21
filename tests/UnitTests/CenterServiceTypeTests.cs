using ResidApp.Application.Errors;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Tipo de servicio del centro (ADR 0017, script 0113): las reglas del dominio, los códigos de la base de datos y los textos de los errores.</summary>
public class CenterServiceTypeTests
{
    [Fact]
    public void Los_codigos_son_los_de_la_base_de_datos_y_son_solo_dos()
    {
        Assert.Equal(["RESIDENCIA", "CENTRO_DE_DIA"], Enum.GetValues<CenterServiceType>().Select(type => type.ToCode()));
        Assert.Equal(CenterServiceType.CentroDeDia, EnumCode.ParseCode<CenterServiceType>("CENTRO_DE_DIA"));
        Assert.Equal(CenterServiceType.Residencia, EnumCode.ParseCode<CenterServiceType>("RESIDENCIA"));
    }

    [Fact]
    public void Sin_elegir_el_tipo_o_con_uno_que_no_existe_no_se_valida_y_no_hay_valor_por_defecto()
    {
        Assert.Equal(CenterServiceTypeRules.Invalid, Assert.Throws<DomainValidationException>(() => CenterServiceTypeRules.Validate(null)).Message);
        Assert.Equal(CenterServiceTypeRules.Invalid, Assert.Throws<DomainValidationException>(() => CenterServiceTypeRules.Validate((CenterServiceType)99)).Message);
        Assert.Equal(CenterServiceType.Residencia, CenterServiceTypeRules.Validate(CenterServiceType.Residencia));
        Assert.Equal(CenterServiceType.CentroDeDia, CenterServiceTypeRules.Validate(CenterServiceType.CentroDeDia));
    }

    [Fact]
    public void Solo_un_centro_sin_personas_se_puede_corregir()
    {
        var empty = new CenterServiceTypeState(CenterServiceType.Residencia, [], HasPeople: false);
        var withPeople = new CenterServiceTypeState(CenterServiceType.CentroDeDia, [], HasPeople: true);

        Assert.True(empty.CanBeCorrected);
        Assert.False(withPeople.CanBeCorrected);
    }

    [Fact]
    public void Un_centro_nuevo_sin_dato_de_tipo_es_una_Residencia()
    {
        // Los centros anteriores al script 0113 y las pruebas que no lo dicen: lo que lleva el registro del alta por defecto, nunca un Centro de Día.
        Assert.Equal(CenterServiceType.Residencia, NewCenter.Validate("cen-1", "Centro", "u1", "Unidad", "admin-1", "Ana").ServiceType);
    }

    [Theory]
    [InlineData("CENTER_SERVICE_TYPE_INVALID", ApplicationFailureCode.InvalidInput)]
    [InlineData("CENTER_SERVICE_TYPE_CENTER_INVALID", ApplicationFailureCode.InvalidInput)]
    [InlineData("CENTER_SERVICE_TYPE_SEQUENCE_INVALID", ApplicationFailureCode.Conflict)]
    [InlineData("CENTER_SERVICE_TYPE_NOT_EMPTY", ApplicationFailureCode.Conflict)]
    [InlineData("CENTER_SERVICE_TYPE_FORBIDDEN", ApplicationFailureCode.AccessDenied)]
    [InlineData("CENTER_SERVICE_TYPE_HISTORY_IMMUTABLE", ApplicationFailureCode.Conflict)]
    [InlineData("CENTER_SERVICE_TYPE_HISTORY_MISSING", ApplicationFailureCode.Conflict)]
    public void Cada_codigo_que_lanza_la_base_de_datos_se_traduce_a_un_fallo_con_su_texto_en_castellano(string code, ApplicationFailureCode expected)
    {
        var failure = ApplicationResultRunner.Normalize(new InvalidOperationException(code));

        Assert.Equal(expected, failure.Code);
        Assert.DoesNotContain("CENTER_SERVICE_TYPE", failure.Message);
        Assert.NotEmpty(failure.Message);
    }

    [Fact]
    public void El_aviso_de_un_centro_con_personas_dice_que_hay_que_crear_un_centro_nuevo()
    {
        var failure = ApplicationResultRunner.Normalize(new InvalidOperationException("CENTER_SERVICE_TYPE_NOT_EMPTY"));

        Assert.Contains("crear un centro nuevo", failure.Message);
    }
}
