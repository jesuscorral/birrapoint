using MediatR;

namespace BirraPoint.Common.Modules;

/// <summary>A request that changes state. Marker only: it adds no pipeline behaviour.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker that separates writes from reads for architecture tests; adds no behaviour.")]
public interface ICommand<out TResponse> : IRequest<TResponse>;
