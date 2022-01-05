﻿using Amazon.Pay.API.WebStore.ChargePermission;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Smartstore.Core;
using Smartstore.Core.Checkout.Orders.Events;
using Smartstore.Engine.Modularity;
using Smartstore.Events;

namespace Smartstore.AmazonPay
{
    public class Events : IConsumer
    {
        public Localizer T { get; set; } = NullLocalizer.Instance;

        public async Task HandleEventAsync(OrderPaidEvent message,
            ICommonServices services,
            IProviderManager providerManager,
            IHttpContextAccessor httpContextAccessor,
            ILogger logger)
        {
            var order = message.Order;
            var httpContext = httpContextAccessor?.HttpContext;

            if (order != null 
                && httpContext != null
                && order.PaymentMethodSystemName.EqualsNoCase(AmazonPayProvider.SystemName)
                && order.AuthorizationTransactionCode.HasValue())
            {
                var module = services.ApplicationContext.ModuleCatalog.GetModuleByAssembly(typeof(Events).Assembly);

                if (providerManager.IsActiveForStore(module, order.StoreId))
                {
                    try
                    {
                        var client = await httpContext.GetAmazonPayApiClientAsync(order.StoreId);
                        var request = new CloseChargePermissionRequest(T("Plugins.Payments.AmazonPay.CloseChargeReason").Value.Truncate(255))
                        {
                            CancelPendingCharges = false
                        };

                        var response = client.CloseChargePermission(order.AuthorizationTransactionCode, request);
                        if (!response.Success)
                        {
                            logger.LogAmazonPayFailure(request, response);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex);
                    }
                }
            }
        }
    }
}
