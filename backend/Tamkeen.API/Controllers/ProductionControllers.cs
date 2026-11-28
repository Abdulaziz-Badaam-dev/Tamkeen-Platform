using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Tamkeen.API.Controllers;

internal sealed class ProductionControllers : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
        feature.Controllers.Remove(typeof(AgentTestController).GetTypeInfo());
}
