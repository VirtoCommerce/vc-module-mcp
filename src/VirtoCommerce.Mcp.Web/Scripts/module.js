// Call this to register your module to main application
var moduleName = 'VirtoCommerce.Mcp';

if (AppDependencies !== undefined) {
    AppDependencies.push(moduleName);
}

angular.module(moduleName, [])
    .config(['$stateProvider',
        function ($stateProvider) {
            $stateProvider
                .state('workspace.McpState', {
                    url: '/mcp',
                    templateUrl: '$(Platform)/Scripts/common/templates/home.tpl.html',
                    controller: [
                        'platformWebApp.bladeNavigationService',
                        function (bladeNavigationService) {
                            var newBlade = {
                                id: 'blade1',
                                controller: 'VirtoCommerce.Mcp.helloWorldController',
                                template: 'Modules/$(VirtoCommerce.Mcp)/Scripts/blades/hello-world.html',
                                isClosingDisabled: true,
                            };
                            bladeNavigationService.showBlade(newBlade);
                        }
                    ]
                });
        }
    ])
    .run(['platformWebApp.mainMenuService', '$state',
        function (mainMenuService, $state) {
            //Register module in main menu
            var menuItem = {
                path: 'browse/mcp',
                icon: 'fa fa-cube',
                title: 'Mcp',
                priority: 100,
                action: function () { $state.go('workspace.McpState'); },
                permission: 'mcp:access',
            };
            mainMenuService.addMenuItem(menuItem);
        }
    ]);
