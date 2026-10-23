using System.Text;

namespace HotelWeb.Core.Ecf;

/// <summary>El XML como lo escribe Delphi (TXMLDocument, doNodeAutoIndent): sin encoding, 2 espacios, CRLF.</summary>
internal sealed class XmlDelphi
{
    private readonly StringBuilder _sb = new("<?xml version=\"1.0\"?>\r\n");
    private readonly Stack<string> _abiertos = new();

    private string Sangria => new(' ', _abiertos.Count * 2);

    public void Abrir(string nombre)
    {
        _sb.Append(Sangria).Append('<').Append(nombre).Append(">\r\n");
        _abiertos.Push(nombre);
    }

    public void Cerrar()
    {
        var n = _abiertos.Pop();
        _sb.Append(Sangria).Append("</").Append(n).Append(">\r\n");
    }

    public void Nodo(string nombre, string valor) =>
        _sb.Append(Sangria).Append('<').Append(nombre).Append('>').Append(Escapar(valor)).Append("</").Append(nombre).Append(">\r\n");

    public string Texto() => _sb.ToString();

    private static string Escapar(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
