namespace Kodvian.Core.Application.Common.Files;

public class StorageUnavailableException : Exception
{
    public StorageUnavailableException() : base("No se pudo guardar la evidencia en el almacenamiento. Verifica tu conexión y reintenta; si persiste, informa la hora y el nombre del archivo.") { }
    public StorageUnavailableException(Exception innerException) : base("No se pudo guardar la evidencia en el almacenamiento. Verifica tu conexión y reintenta; si persiste, informa la hora y el nombre del archivo.", innerException) { }
}
