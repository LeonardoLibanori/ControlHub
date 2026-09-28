using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace tests.QA;

/// <summary>
/// Deixa um Controller utilizavel FORA do pipeline HTTP.
/// Sem HttpContext, TempData lanca NullReferenceException ao ser escrito
/// (HomeController.AjustarEstoque, linha 164), o que mascararia o bug real.
/// </summary>
public static class ControllerTestExtensions
{
    public static T ComTempData<T>(this T controller) where T : Controller
    {
        var httpContext = new DefaultHttpContext();
        var provider = new DictionaryTempDataProvider();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor()
        };

        controller.TempData = new TempDataDictionary(httpContext, provider);
        return controller;
    }

    /// <summary>ITempDataProvider simples, backed por dicionario em memoria.</summary>
    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context)
            => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            // Nada a persistir: o dicionario vive apenas durante o teste.
        }
    }
}

/// <summary>Helpers para inspecionar os objetos anonimos devolvidos por Json(...)/JsonResult.</summary>
public static class JsonAssertExtensions
{
    /// <summary>Extrai o valor de uma propriedade do objeto anonimo de um JsonResult.</summary>
    public static object? GetProp(this IActionResult result, string name)
    {
        var value = Unwrap(result);
        Assert.NotNull(value);
        var prop = value!.GetType().GetProperty(name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        Assert.True(prop is not null, $"Propriedade '{name}' nao existe no JSON devolvido. " +
            $"Propriedades: {string.Join(", ", value.GetType().GetProperties().Select(p => p.Name))}");
        return prop!.GetValue(value);
    }

    /// <summary>Extrai uma propriedade numerica (double/decimal/int) do JsonResult.</summary>
    public static double GetNum(this IActionResult result, string name)
    {
        var raw = result.GetProp(name);
        return Convert.ToDouble(raw);
    }

    /// <summary>Extrai uma propriedade textual do JsonResult.</summary>
    public static string? GetStr(this IActionResult result, string name)
        => result.GetProp(name)?.ToString();

    /// <summary>True quando o JSON traz success = true.</summary>
    public static bool IsSuccess(this IActionResult result)
        => result.GetProp("success") is true;

    /// <summary>Devolve o corpo JSON bruto (string) para busca de texto vazado.</summary>
    public static string RawJson(this IActionResult result)
    {
        var value = Unwrap(result);
        Assert.NotNull(value);
        return System.Text.Json.JsonSerializer.Serialize(value);
    }

    /// <summary>Desembrulha JsonResult/ObjectResult ate o objeto anonimo real.</summary>
    private static object? Unwrap(IActionResult result)
    {
        return result switch
        {
            JsonResult jr => jr.Value,
            ObjectResult or_ => or_.Value,
            _ => null
        };
    }
}
