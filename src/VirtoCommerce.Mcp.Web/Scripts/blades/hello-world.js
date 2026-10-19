angular.module('VirtoCommerce.Mcp')
    .controller('VirtoCommerce.Mcp.helloWorldController', ['$scope', 'VirtoCommerce.Mcp.webApi', function ($scope, api) {
        var blade = $scope.blade;
        blade.title = 'Mcp';

        blade.refresh = function () {
            api.get(function (data) {
                blade.title = 'mcp.blades.hello-world.title';
                blade.data = data.result;
                blade.isLoading = false;
            });
        };

        blade.refresh();
    }]);
