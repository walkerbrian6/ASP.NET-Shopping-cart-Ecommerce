﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Smartstore.Caching;
using Smartstore.Core.Catalog;
using Smartstore.Core.Catalog.Attributes;
using Smartstore.Core.Catalog.Brands;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Discounts;
using Smartstore.Core.Catalog.Pricing;
using Smartstore.Core.Catalog.Pricing.Calculators;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Cart;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Common;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Identity;
using Smartstore.Core.Localization;
using Smartstore.Core.Stores;
using Smartstore.Engine;
using Smartstore.Test.Common;

namespace Smartstore.Core.Tests.Catalog.Pricing
{
    [TestFixture]
    public class PriceCalculationServiceTests : ServiceTest
    {
        IPriceCalculationService _priceCalcService;

        IStoreContext _storeContext;
        IWorkContext _workContext;
        IPriceCalculatorFactory _priceCalculatorFactory;
        ITaxCalculator _taxCalculator;
        IProductAttributeMaterializer _productAttributeMaterializer;
        IProductService _productService;
        ICategoryService _categoryService;
        IManufacturerService _manufacturerService;
        IDiscountService _discountService;

        ICurrencyService _currencyService;
        ICommonServices _services;
        ITaxService _taxService;
        IRequestCache _requestCache;

        CatalogSettings _catalogSettings;
        TaxSettings _taxSettings;
        Store _store;
        Currency _currency;
        Customer _customer;
        Language _language;
        Product _product;
        ShoppingCartItem _sci;

        PriceCalculationContext _priceCalculationContext;
        ProductBatchContext _productBatchContext;

        [OneTimeSetUp]
        public new void SetUp()
        {
            _store = new Store { Id = 1 };
            _currency = new Currency { Id = 1 };
            _customer = new Customer { Id = 1 };
            _language = new Language { Id = 1 };

            var storeContextWrapper = new Mock<IStoreContext>();
            _storeContext = storeContextWrapper.Object;
            storeContextWrapper.Setup(x => x.CurrentStore).Returns(_store);

            var workContextWrapper = new Mock<IWorkContext>();
            _workContext = workContextWrapper.Object;
            workContextWrapper.Setup(x => x.WorkingCurrency).Returns(_currency);
            workContextWrapper.Setup(x => x.WorkingLanguage).Returns(_language);

            _requestCache = new NullRequestCache();

            var productServiceWrapper = new Mock<IProductService>();
            _productService = productServiceWrapper.Object;

            var categoryServiceWrapper = new Mock<ICategoryService>();
            _categoryService = categoryServiceWrapper.Object;

            var manufacturerServiceWrapper = new Mock<IManufacturerService>();
            _manufacturerService = manufacturerServiceWrapper.Object;

            var builder = new ContainerBuilder();
            builder.RegisterInstance(_productService).As<IProductService>().SingleInstance();
            builder.RegisterInstance(_categoryService).As<ICategoryService>().SingleInstance();
            builder.RegisterInstance(_manufacturerService).As<IManufacturerService>().SingleInstance();

            _services = new MockCommonServices(DbContext, builder.Build());

            var productAttributeMaterializerWrapper = new Mock<IProductAttributeMaterializer>();
            _productAttributeMaterializer = productAttributeMaterializerWrapper.Object;

            var taxServiceWrapper = new Mock<ITaxService>();
            _taxService = taxServiceWrapper.Object;

            var discountServiceWrapper = new Mock<IDiscountService>();
            _discountService = discountServiceWrapper.Object;
            discountServiceWrapper.Setup(x => x.GetAllDiscountsAsync(DiscountType.AssignedToCategories, null, false)).ReturnsAsync(new List<Discount>());
            discountServiceWrapper.Setup(x => x.GetAllDiscountsAsync(DiscountType.AssignedToManufacturers, null, false)).ReturnsAsync(new List<Discount>());
            // INFO: Extensions methods can't be mocked.
            //discountServiceWrapper.Setup(x => x.IsDiscountValidAsync(null, _customer, _store)).ReturnsAsync(true);

            var currencyServiceWrapper = new Mock<ICurrencyService>();
            _currencyService = currencyServiceWrapper.Object;
            currencyServiceWrapper.Setup(x => x.PrimaryCurrency).Returns(_currency);
            currencyServiceWrapper.Setup(x => x.PrimaryExchangeCurrency).Returns(_currency);

            _catalogSettings = new CatalogSettings();
            _taxSettings = new TaxSettings();

            // INFO: no mocking here to use real implementation.
            _taxCalculator = new TaxCalculator(DbContext, _workContext, _taxService, _taxSettings);

            // INFO: Create real instance of PriceCalculatorFactory with own instances of Calculators
            _priceCalculatorFactory = new PriceCalculatorFactory(_requestCache, GetCalculators());

            _priceCalcService = new PriceCalculationService(
                DbContext,
                _workContext, 
                _storeContext,
                _priceCalculatorFactory,
                _taxCalculator,
                _productService,
                _productAttributeMaterializer,
                _taxService,
                _currencyService,
                _catalogSettings,
                _taxSettings);
        }

        [SetUp]
        public async Task Reset()
        {
            _product = new Product
            {
                Id = 1,
                Name = "Product name 1",
                Price = 12.34M,
                CustomerEntersPrice = false,
                Published = true,
                ProductType = ProductType.SimpleProduct
            };

            _sci = new ShoppingCartItem()
            {
                Customer = _customer,
                CustomerId = _customer.Id,
                Product = _product,
                ProductId = _product.Id,
                Quantity = 2,
            };

            _productBatchContext = new ProductBatchContext(new List<Product> { _product }, _services, _store, _customer, false);
            _priceCalculationContext = new PriceCalculationContext(_product, new PriceCalculationOptions(_productBatchContext, _customer, _store, _language, _currency)
            {
                IgnoreDiscounts = true
            })
            {
                Quantity = 1,
                AdditionalCharge = 0
            };

            DbContext.Discounts.Remove(1);
            DbContext.Products.Remove(1);
            await DbContext.SaveChangesAsync();
        }

        private void InitTierPrices()
        {
            _product.TierPrices.Add(new TierPrice
            {
                Id = 1,
                Price = 10,
                Quantity = 2,
                Product = _product,
                CalculationMethod = TierPriceCalculationMethod.Fixed
            });

            _product.TierPrices.Add(new TierPrice
            {
                Id = 2,
                Price = 8,
                Quantity = 5,
                Product = _product,
                CalculationMethod = TierPriceCalculationMethod.Fixed
            });

            _product.TierPrices.Add(new TierPrice
            {
                Id = 3,
                Price = 1,
                Quantity = 10,
                Product = _product,
                CalculationMethod = TierPriceCalculationMethod.Adjustment
            });

            _product.TierPrices.Add(new TierPrice
            {
                Id = 4,
                Price = 50,
                Quantity = 20,
                Product = _product,
                CalculationMethod = TierPriceCalculationMethod.Percental
            });

            // set HasTierPrices property
            _product.HasTierPrices = true;

            DbContext.Products.Add(_product);
        }
        private void InitTierPricesForCustomerRoles()
        {
            var customerRole1 = new CustomerRole
            {
                Id = 1,
                Name = "Some role 1",
                Active = true,
            };

            var customerRole2 = new CustomerRole
            {
                Id = 2,
                Name = "Some role 2",
                Active = true,
            };

            DbContext.CustomerRoles.Add(customerRole1);
            DbContext.CustomerRoles.Add(customerRole2);

            _product.TierPrices.Add(new TierPrice
            {
                Price = 10,
                Quantity = 2,
                Product = _product,
                CustomerRole = customerRole1,
                CalculationMethod = TierPriceCalculationMethod.Fixed
            });

            _product.TierPrices.Add(new TierPrice
            {
                Price = 9,
                Quantity = 2,
                Product = _product,
                CustomerRole = customerRole2,
                CalculationMethod = TierPriceCalculationMethod.Fixed
            });

            _product.TierPrices.Add(new TierPrice
            {
                Price = 8,
                Quantity = 5,
                Product = _product,
                CustomerRole = customerRole1,
                CalculationMethod = TierPriceCalculationMethod.Fixed
            });

            _product.TierPrices.Add(new TierPrice
            {
                Price = 5,
                Quantity = 10,
                Product = _product,
                CustomerRole = customerRole2,
                CalculationMethod = TierPriceCalculationMethod.Fixed
            });

            // set HasTierPrices property
            _product.HasTierPrices = true;

            DbContext.Products.Add(_product);
            DbContext.Customers.Add(_customer);

            _customer.CustomerRoleMappings.Add(new CustomerRoleMapping
            {
                CustomerId = _customer.Id,
                CustomerRoleId = customerRole1.Id,
                CustomerRole = customerRole1
            });    
        }

        private List<Lazy<IPriceCalculator, PriceCalculatorMetadata>> GetCalculators()
        {
            var calculators = new List<Lazy<IPriceCalculator, PriceCalculatorMetadata>>();
            var productMetadata = new PriceCalculatorMetadata { ValidTargets = CalculatorTargets.Product, Order = CalculatorOrdering.Default + 10 };
            var bundleMetadata = new PriceCalculatorMetadata { ValidTargets = CalculatorTargets.Bundle, Order = CalculatorOrdering.Early };
            var allMetadata = new PriceCalculatorMetadata { ValidTargets = CalculatorTargets.All, Order = CalculatorOrdering.Late };
            var groupedMetadata = new PriceCalculatorMetadata { ValidTargets = CalculatorTargets.GroupedProduct, Order = CalculatorOrdering.Early };
            var productOrBundleMetadata = new PriceCalculatorMetadata { ValidTargets = CalculatorTargets.Product | CalculatorTargets.Bundle, Order = CalculatorOrdering.Default };

            var attributePriceCalculator = new Lazy<IPriceCalculator, PriceCalculatorMetadata>(() => 
                new AttributePriceCalculator(_priceCalculatorFactory, DbContext), productMetadata);
            calculators.Add(attributePriceCalculator);

            // TODO: (mh) (core) Mock ProductService
            var bundlePriceCalculator = new Lazy<IPriceCalculator, PriceCalculatorMetadata>(() => 
                new BundlePriceCalculator(_priceCalculatorFactory, null), bundleMetadata);
            calculators.Add(bundlePriceCalculator);

            var discountPriceCalculator = new Lazy<IPriceCalculator, PriceCalculatorMetadata>(() 
                => new DiscountPriceCalculator(DbContext, _discountService, _catalogSettings), allMetadata);
            calculators.Add(discountPriceCalculator);

            // TODO: (mh) (core) Mock CatalogSearchService & ProductService
            var groupedProductPriceCalculator = new Lazy<IPriceCalculator, PriceCalculatorMetadata>(() => 
                new GroupedProductPriceCalculator(null, _priceCalculatorFactory, null), groupedMetadata);
            calculators.Add(groupedProductPriceCalculator);

            var lowestPriceCalculator = new Lazy<IPriceCalculator, PriceCalculatorMetadata>(() =>
                new LowestPriceCalculator(), productMetadata);
            calculators.Add(lowestPriceCalculator);

            var offerPriceCalculator = new Lazy<IPriceCalculator, PriceCalculatorMetadata>(() =>
                new OfferPriceCalculator(), productOrBundleMetadata);
            calculators.Add(offerPriceCalculator);

            // TODO: (mh) (core) Mock Materializer
            productMetadata.Order = CalculatorOrdering.Early + 1;
            var preselectedPriceCalculator = new Lazy<IPriceCalculator, PriceCalculatorMetadata>(() =>
                new PreselectedPriceCalculator(null), productMetadata);
            calculators.Add(preselectedPriceCalculator);

            productMetadata.Order = CalculatorOrdering.Default + 100;
            var tierPriceCalculator = new Lazy<IPriceCalculator, PriceCalculatorMetadata>(() =>
                new TierPriceCalculator(), productMetadata);
            calculators.Add(tierPriceCalculator);

            return calculators;
        }

        [Test]
        public async Task Can_get_final_product_price()
        {
            var price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(12.34M);

            _priceCalculationContext.Quantity = 2;
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);

            price.FinalPrice.Amount.ShouldEqual(12.34M);
        }

        [Test]
        public async Task Can_get_final_product_price_with_tier_prices()
        {
            InitTierPrices();

            await DbContext.SaveChangesAsync();

            _priceCalculationContext.Options.IgnoreDiscounts = false;
            _priceCalculationContext.Options.IgnoreTierPrices = false;

            _priceCalculationContext.Quantity = 1;
            _priceCalculationContext.AdditionalCharge = 0;

            var price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(12.34M);

            _priceCalculationContext.Quantity = 2;
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(10);

            _priceCalculationContext.Quantity = 3;
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(10);

            _priceCalculationContext.Quantity = 5;
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(8);

            _priceCalculationContext.Quantity = 10;
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(11.34M);

            _priceCalculationContext.Quantity = 20;
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(6.17M);
        }

        [Test]
        public async Task Can_get_final_product_price_with_tier_prices_by_customerRole()
        {
            InitTierPricesForCustomerRoles();

            await DbContext.SaveChangesAsync();

            _priceCalculationContext.Options.IgnoreDiscounts = false;
            _priceCalculationContext.Options.IgnoreTierPrices = false;

            var price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(12.34M);

            _priceCalculationContext.Quantity = 2;
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(10);

            _priceCalculationContext.Quantity = 3;
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(10);

            _priceCalculationContext.Quantity = 5;
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(8);

            _priceCalculationContext.Quantity = 10;
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(8);
        }

        // INFO: This test fails cause there seems to be no use case for this anymore
        // TODO: (mg) (core) As discussed decide for yourself whether to delete or support this somehow.
        //[Test]
        //public async Task Can_get_final_product_price_with_additionalFee()
        //{
        //    _priceCalculationContext.Options.IgnoreDiscounts = false;
        //    _priceCalculationContext.Options.IgnoreTierPrices = false;

        //    _priceCalculationContext.AdditionalCharge = 5;

        //    var price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
        //    price.FinalPrice.Amount.ShouldEqual(17.34M);
        //}

        [Test]
        public async Task Can_get_final_product_price_with_discount()
        {
            _priceCalculationContext.Options.IgnoreDiscounts = false;
            _priceCalculationContext.Options.CheckDiscountValidity = false;

            var discount1 = new Discount()
            {
                Id = 1,
                Name = "Discount 1",
                DiscountType = DiscountType.AssignedToSkus,
                DiscountAmount = 3,
                DiscountLimitation = DiscountLimitationType.Unlimited
            };

            discount1.AppliedToProducts.Add(_product);
            _product.AppliedDiscounts.Add(discount1);
            _product.HasDiscountsApplied = true;

            DbContext.Discounts.Add(discount1);
            DbContext.Products.Add(_product);
            await DbContext.SaveChangesAsync();

            var price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(9.34M);
        }

        [Test]
        public async Task Can_get_final_product_price_with_special_price()
        {
            _priceCalculationContext.Options.IgnoreOfferPrice = false;

            _priceCalculationContext.Product.SpecialPrice = 10.01M;
            _priceCalculationContext.Product.SpecialPriceStartDateTimeUtc = DateTime.UtcNow.AddDays(-1);
            _priceCalculationContext.Product.SpecialPriceEndDateTimeUtc = DateTime.UtcNow.AddDays(1);

            var price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(10.01M);

            // Invalid date
            _product.SpecialPriceStartDateTimeUtc = DateTime.UtcNow.AddDays(1);
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(12.34M);

            // No dates
            _product.SpecialPriceStartDateTimeUtc = null;
            _product.SpecialPriceEndDateTimeUtc = null;
            price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(10.01M);
        }

        [Test]
        public async Task Can_get_final_product_price_with_variant_combination_price()
        {
            var combination = new ProductVariantAttributeCombination
            {
                Id = 1,
                Price = 18.90M,
                ProductId = 1
            };

            _product.MergeWithCombination(combination);

            var price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);
            price.FinalPrice.Amount.ShouldEqual(18.90M);
        }

        [Test]
        public async Task Can_get_product_discount()
        {
            _priceCalculationContext.Options.IgnoreDiscounts = false;
            _priceCalculationContext.Options.CheckDiscountValidity = false;

            var discount1 = new Discount()
            {
                Id = 1,
                Name = "Discount 1",
                DiscountType = DiscountType.AssignedToSkus,
                DiscountAmount = 3,
                DiscountLimitation = DiscountLimitationType.Unlimited
            };
            discount1.AppliedToProducts.Add(_product);
            _product.AppliedDiscounts.Add(discount1);
            _product.HasDiscountsApplied = true;

            var discount2 = new Discount()
            {
                Id = 2,
                Name = "Discount 2",
                DiscountType = DiscountType.AssignedToSkus,
                DiscountAmount = 4,
                DiscountLimitation = DiscountLimitationType.Unlimited
            };
            discount2.AppliedToProducts.Add(_product);
            _product.AppliedDiscounts.Add(discount2);

            var discount3 = new Discount()
            {
                Id = 3,
                Name = "Discount 3",
                DiscountType = DiscountType.AssignedToOrderSubTotal,
                DiscountAmount = 5,
                DiscountLimitation = DiscountLimitationType.Unlimited,
                RequiresCouponCode = true,
                CouponCode = "SECRET CODE"
            };
            discount3.AppliedToProducts.Add(_product);
            _product.AppliedDiscounts.Add(discount3);

            DbContext.Discounts.Add(discount1);
            DbContext.Discounts.Add(discount2);
            DbContext.Discounts.Add(discount3);
            DbContext.Products.Add(_product);
            await DbContext.SaveChangesAsync();

            var price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);

            price.DiscountAmount.Amount.ShouldEqual(4);
            price.AppliedDiscounts.FirstOrDefault().ShouldNotBeNull();
            price.AppliedDiscounts.FirstOrDefault().ShouldEqual(discount2);
        }

        [Test]
        public async Task Ensure_discount_is_not_applied_to_products_with_prices_entered_by_customer()
        {
            _priceCalculationContext.Options.IgnoreDiscounts = false;
            _priceCalculationContext.Options.CheckDiscountValidity = false;

            _product.CustomerEntersPrice = true;

            var discount1 = new Discount()
            {
                Id = 1,
                Name = "Discount 1",
                DiscountType = DiscountType.AssignedToSkus,
                DiscountAmount = 3,
                DiscountLimitation = DiscountLimitationType.Unlimited
            };
            discount1.AppliedToProducts.Add(_product);
            _product.AppliedDiscounts.Add(discount1);
            _product.HasDiscountsApplied = true;

            DbContext.Discounts.Add(discount1);
            DbContext.Products.Add(_product);
            await DbContext.SaveChangesAsync();

            var price = await _priceCalcService.CalculatePriceAsync(_priceCalculationContext);

            price.DiscountAmount.Amount.ShouldEqual(0);
            price.AppliedDiscounts.FirstOrDefault().ShouldBeNull();
        }

        [Test]
        public async Task Can_get_shopping_cart_item_unitPrice()
        {
            var item = new OrganizedShoppingCartItem(_sci);
            var calculationOptions = _priceCalcService.CreateDefaultOptions(false, _customer, _currency, _productBatchContext);
            var calculationContext = await _priceCalcService.CreateCalculationContextAsync(item, calculationOptions);
            var (unitPrice, _) = await _priceCalcService.CalculateSubtotalAsync(calculationContext);

            unitPrice.FinalPrice.Amount.ShouldEqual(12.34);
        }

        [Test]
        public async Task Can_get_shopping_cart_item_subTotal()
        {
            var item = new OrganizedShoppingCartItem(_sci);
            var calculationOptions = _priceCalcService.CreateDefaultOptions(false, _customer, _currency, _productBatchContext);
            var calculationContext = await _priceCalcService.CreateCalculationContextAsync(item, calculationOptions);
            var (_, subtotal) = await _priceCalcService.CalculateSubtotalAsync(calculationContext);

            subtotal.FinalPrice.Amount.ShouldEqual(24.68);
        }
    }
}