// Call this to register your module to main application
var moduleName = 'VirtoCommerce.MCP';

if (AppDependencies !== undefined) {
    AppDependencies.push(moduleName);
}

angular.module(moduleName, []);
