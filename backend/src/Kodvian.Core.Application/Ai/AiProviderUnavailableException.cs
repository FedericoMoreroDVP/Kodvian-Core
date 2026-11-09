namespace Kodvian.Core.Application.Ai;

public sealed class AiProviderUnavailableException(string message = "La Oficina Inteligente no está disponible temporalmente.") : Exception(message);

public sealed class AiLiveDataApprovalRequiredException() : Exception("El Administrador debe aprobar el uso de datos reales en la configuración de IA.");

public sealed class AiConversationConflictException(string message) : Exception(message);

public sealed class AiToolRejectedException(string message) : Exception(message);
