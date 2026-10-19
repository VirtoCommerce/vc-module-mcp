angular.module('VirtoCommerce.Mcp')
    .factory('VirtoCommerce.Mcp.webApi', ['$resource', function ($resource) {
        return $resource('api/mcp');
    }]);
