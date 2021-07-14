﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Admin.Models.Customers;
using Smartstore.ComponentModel;
using Smartstore.Core.Catalog.Brands;
using Smartstore.Core.Catalog.Categories;
using Smartstore.Core.Catalog.Products;
using Smartstore.Core.Checkout.Tax;
using Smartstore.Core.Content.Topics;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;
using Smartstore.Core.Identity.Rules;
using Smartstore.Core.Localization;
using Smartstore.Core.Logging;
using Smartstore.Core.Rules;
using Smartstore.Core.Security;
using Smartstore.Data;
using Smartstore.Scheduling;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling;
using Smartstore.Web.Modelling.DataGrid;
using Smartstore.Web.Rendering;

namespace Smartstore.Admin.Controllers
{
    public class CustomerRoleController : AdminControllerBase
    {
        private readonly SmartDbContext _db;
        private readonly RoleManager<CustomerRole> _roleManager;
        private readonly IRuleService _ruleService;
        private readonly Lazy<ITaskStore> _taskStore;
        private readonly Lazy<ITaskScheduler> _taskScheduler;
        private readonly CustomerSettings _customerSettings;
        
        public CustomerRoleController(
            SmartDbContext db,
            RoleManager<CustomerRole> roleStore,
            IRuleService ruleService,
            Lazy<ITaskStore> taskStore,
            Lazy<ITaskScheduler> taskScheduler,
            CustomerSettings customerSettings)
        {
            _db = db;
            _roleManager = roleStore;
            _taskStore = taskStore;
            _ruleService = ruleService;
            _taskScheduler = taskScheduler;
            _customerSettings = customerSettings;
        }

        /// <summary>
        /// (AJAX) Gets a list of all available customer roles. 
        /// </summary>
        /// <param name="label">Text for optional entry. If not null an entry with the specified label text and the Id 0 will be added to the list.</param>
        /// <param name="selectedIds">Ids of selected entities.</param>
        /// <param name="includeSystemRoles">Specifies whether to include system roles.</param>
        /// <returns>List of all customer roles as JSON.</returns>
        public async Task<IActionResult> AllCustomerRoles(string label, string selectedIds, bool? includeSystemRoles)
        {
            var query = _roleManager.Roles.AsNoTracking();
            
            if (!(includeSystemRoles ?? true))
            {
                query = query.Where(x => x.IsSystemRole);
            }

            query = query.ApplyStandardFilter(true);

            var rolesPager = new FastPager<CustomerRole>(query, 500);
            var customerRoles = new List<CustomerRole>();
            var ids = selectedIds.ToIntArray();

            while ((await rolesPager.ReadNextPageAsync<CustomerRole>()).Out(out var roles))
            {
                customerRoles.AddRange(roles);
            }

            var list = customerRoles
                .OrderBy(x => x.Name)
                .Select(x => new ChoiceListItem
                {
                    Id = x.Id.ToString(),
                    Text = x.Name,
                    Selected = ids.Contains(x.Id)
                })
                .ToList();

            if (label.HasValue())
            {
                list.Insert(0, new ChoiceListItem
                {
                    Id = "0",
                    Text = label,
                    Selected = false
                });
            }

            return new JsonResult(list);
        }

        public IActionResult Index()
        {
            return RedirectToAction("List");
        }

        [Permission(Permissions.Customer.Role.Read)]
        public IActionResult List()
        {
            return View();
        }

        [Permission(Permissions.Customer.Role.Read)]
        public async Task<IActionResult> RolesList(GridCommand command)
        {
            var mapper = MapperFactory.GetMapper<CustomerRole, CustomerRoleModel>();

            var customerRoles = await _roleManager.Roles
                .AsNoTracking()
                .ApplyGridCommand(command, false)
                .ToPagedList(command)
                .LoadAsync();

            var rows = await customerRoles.SelectAsync(x =>
            {
                return mapper.MapAsync(x);
            })
            .AsyncToList();

            var gridModel = new GridModel<CustomerRoleModel>
            {
                Rows = rows,
                Total = customerRoles.TotalCount
            };

            return Json(gridModel);
        }

        [Permission(Permissions.Customer.Role.Create)]
        public async Task<IActionResult> Create()
        {
            var model = new CustomerRoleModel
            {
                Active = true
            };

            await PrepareViewBag(model, null);

            return View(model);
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        [Permission(Permissions.Customer.Role.Create)]
        public async Task<IActionResult> Create(CustomerRoleModel model, bool continueEditing)
        {
            if (ModelState.IsValid)
            {
                var role = MiniMapper.Map<CustomerRoleModel, CustomerRole>(model);
                await _ruleService.ApplyRuleSetMappingsAsync(role, model.SelectedRuleSetIds);
    
                await _roleManager.CreateAsync(role);

                // TODO: (mg) (core) test mapping CustomerRoleModel > CustomerRole.

                Services.ActivityLogger.LogActivity(KnownActivityLogTypes.AddNewCustomerRole, T("ActivityLog.AddNewCustomerRole"), role.Name);
                NotifySuccess(T("Admin.Customers.CustomerRoles.Added"));

                return continueEditing 
                    ? RedirectToAction("Edit", new { id = role.Id }) 
                    : RedirectToAction("List");
            }

            return View(model);
        }

        [Permission(Permissions.Customer.Role.Read)]
        public async Task<IActionResult> Edit(int id)
        {
            var role = await _roleManager.FindByIdAsync(id.ToString());
            if (role == null)
            {
                return NotFound();
            }

            var mapper = MapperFactory.GetMapper<CustomerRole, CustomerRoleModel>();
            var model = await mapper.MapAsync(role);

            await PrepareViewBag(model, role);

            return View(model);
        }

        public async Task<IActionResult> InlineEdit(CustomerRoleModel model)
        {
            var success = false;

            if (await Services.Permissions.AuthorizeAsync(Permissions.Customer.Role.Update))
            {
                var role = await _roleManager.FindByIdAsync(model.Id.ToString());
                if (role != null)
                {
                    Validate(model, role);

                    if (ModelState.IsValid)
                    {
                        MiniMapper.Map(model, role);

                        var result = await _roleManager.UpdateAsync(role);
                        success = result.Succeeded;
                    }
                    else
                    {
                        var modelStateErrors = ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage);
                        NotifyError(string.Join(Environment.NewLine, modelStateErrors));
                    }
                }
            }
            else
            {
                NotifyError(await Services.Permissions.GetUnauthorizedMessageAsync(Permissions.Customer.Role.Update));
            }

            return Json(new { success });
        }

        [HttpPost, ParameterBasedOnFormName("save-continue", "continueEditing")]
        [Permission(Permissions.Customer.Role.Update)]
        public async Task<IActionResult> Edit(CustomerRoleModel model, bool continueEditing, IFormCollection form)
        {
            var role = await _roleManager.FindByIdAsync(model.Id.ToString());
            if (role == null)
            {
                return NotFound();
            }

            Validate(model, role);

            if (ModelState.IsValid)
            {
                try
                {
                    MiniMapper.Map(model, role);
                    await _ruleService.ApplyRuleSetMappingsAsync(role, model.SelectedRuleSetIds);

                    await _roleManager.UpdateAsync(role);

                    // INFO: cached permission tree removed by PermissionRoleMappingHook.
                    await UpdatePermissionRoleMappings(role, form);

                    Services.ActivityLogger.LogActivity(KnownActivityLogTypes.EditCustomerRole, T("ActivityLog.EditCustomerRole"), role.Name);
                    NotifySuccess(T("Admin.Customers.CustomerRoles.Updated"));

                    return continueEditing
                        ? RedirectToAction("Edit", new { id = role.Id })
                        : RedirectToAction("List");
                }
                catch (Exception ex)
                {
                    NotifyError(ex);
                }
            }

            return RedirectToAction("Edit", new { id = role.Id });
        }

        [HttpPost]
        [Permission(Permissions.Customer.Role.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var role = await _roleManager.FindByIdAsync(id.ToString());
            if (role == null)
            {
                return NotFound();
            }

            try
            {
                await _roleManager.DeleteAsync(role);

                Services.ActivityLogger.LogActivity(KnownActivityLogTypes.DeleteCustomerRole, T("ActivityLog.DeleteCustomerRole"), role.Name);
                NotifySuccess(T("Admin.Customers.CustomerRoles.Deleted"));

                return RedirectToAction("List");
            }
            catch (Exception ex)
            {
                NotifyError(ex.Message);
                return RedirectToAction("Edit", new { id = role.Id });
            }
        }

        [Permission(Permissions.Customer.Role.Read)]
        public async Task<IActionResult> CustomerRoleMappingsList(GridCommand command, int id)
        {
            // TODO: (mg) (core) sorting cannot work here (column names do not match property names)
            var customerRoleMappings = await _db.CustomerRoleMappings
                .AsNoTracking()
                .Include(x => x.Customer)
                .ApplyStandardFilter(new[] { id })
                .ApplyGridCommand(command, false)
                .ToPagedList(command)
                .LoadAsync();

            var role = await _roleManager.FindByIdAsync(id.ToString());
            var isGuestRole = role.SystemName.EqualsNoCase(SystemCustomerRoleNames.Guests);
            var emailFallbackStr = isGuestRole ? T("Admin.Customers.Guest").Value : string.Empty;

            var rows = customerRoleMappings.Select(x =>
            {
                var mappingModel = new CustomerRoleMappingModel
                {
                    Id = x.Id,
                    Active = x.Customer.Active,
                    CustomerId = x.CustomerId,
                    Email = x.Customer.Email.NullEmpty() ?? emailFallbackStr,
                    Username = x.Customer.Username,
                    FullName = x.Customer.GetFullName(),
                    CreatedOn = Services.DateTimeHelper.ConvertToUserTime(x.Customer.CreatedOnUtc, DateTimeKind.Utc),
                    LastActivityDate = Services.DateTimeHelper.ConvertToUserTime(x.Customer.LastActivityDateUtc, DateTimeKind.Utc),
                    IsSystemMapping = x.IsSystemMapping,
                    EditUrl = Url.Action("Edit", "Customer", new { id = x.CustomerId, area = "Admin"})
                };

                return mappingModel;
            })
            .ToList();

            var gridModel = new GridModel<CustomerRoleMappingModel>
            {
                Rows = rows,
                Total = customerRoleMappings.TotalCount
            };

            return Json(gridModel);
        }

        [HttpPost]
        [Permission(Permissions.Customer.Role.Update)]
        public async Task<IActionResult> ApplyRules(int id)
        {
            var role = await _roleManager.FindByIdAsync(id.ToString());
            if (role == null)
            {
                return NotFound();
            }

            var task = await _taskStore.Value.GetTaskByTypeAsync<TargetGroupEvaluatorTask>();
            if (task != null)
            {
                await _taskScheduler.Value.RunSingleTaskAsync(task.Id, new Dictionary<string, string>
                {
                    { "CustomerRoleIds", role.Id.ToString() }
                });

                NotifyInfo(T("Admin.System.ScheduleTasks.RunNow.Progress"));
            }
            else
            {
                NotifyError(T("Admin.System.ScheduleTasks.TaskNotFound", nameof(TargetGroupEvaluatorTask)));
            }

            return RedirectToAction("Edit", new { id = role.Id });
        }

        private async Task PrepareViewBag(CustomerRoleModel model, CustomerRole role)
        {
            Guard.NotNull(model, nameof(model));

            if (role != null)
            {
                var showRuleApplyButton = model.SelectedRuleSetIds.Any();

                if (!showRuleApplyButton)
                {
                    // Ignore deleted customers.
                    showRuleApplyButton = await _db.CustomerRoleMappings
                        .AsNoTracking()
                        .Include(x => x.Customer)
                        .Where(x => x.CustomerRoleId == role.Id && x.IsSystemMapping && x.Customer != null)
                        .AnyAsync();
                }

                ViewBag.ShowRuleApplyButton = showRuleApplyButton;
                ViewBag.PermissionTree = await Services.Permissions.GetPermissionTreeAsync(role, true);
                ViewBag.PrimaryStoreCurrencyCode = Services.StoreContext.CurrentStore.PrimaryStoreCurrency.CurrencyCode;
            }

            ViewBag.TaxDisplayTypes = model.TaxDisplayType.HasValue
                ? ((TaxDisplayType)model.TaxDisplayType.Value).ToSelectList()
                : TaxDisplayType.IncludingTax.ToSelectList(false);

            ViewBag.UsernamesEnabled = _customerSettings.CustomerLoginType != CustomerLoginType.Email;
        }

        private async Task<int> UpdatePermissionRoleMappings(CustomerRole role, IFormCollection form)
        {
            await _db.LoadCollectionAsync(role, x => x.PermissionRoleMappings);

            var save = false;
            var permissionKey = "permission-";
            var existingMappings = role.PermissionRoleMappings.ToDictionarySafe(x => x.PermissionRecordId, x => x);

            var mappings = form.Keys.Where(x => x.StartsWith(permissionKey))
                .Select(x =>
                {
                    var id = x[permissionKey.Length..].ToInt();
                    bool? allow = null;
                    var value = form[x].ToString().EmptyNull();
                    if (value.StartsWith("2"))
                    {
                        allow = true;
                    }
                    else if (value.StartsWith("1"))
                    {
                        allow = false;
                    }

                    return new { id, allow };
                })
                .ToDictionary(x => x.id, x => x.allow);

            foreach (var item in mappings)
            {
                if (existingMappings.TryGetValue(item.Key, out var mapping))
                {
                    if (item.Value.HasValue)
                    {
                        if (mapping.Allow != item.Value.Value)
                        {
                            mapping.Allow = item.Value.Value;
                            save = true;
                        }
                    }
                    else
                    {
                        _db.PermissionRoleMappings.Remove(mapping);
                        save = true;
                    }
                }
                else if (item.Value.HasValue)
                {
                    _db.PermissionRoleMappings.Add(new PermissionRoleMapping
                    {
                        Allow = item.Value.Value,
                        PermissionRecordId = item.Key,
                        CustomerRoleId = role.Id
                    });
                    save = true;
                }
            }

            if (save)
            {
                return await _db.SaveChangesAsync();
            }

            return 0;
        }

        private void Validate(CustomerRoleModel model, CustomerRole role)
        {
            if (role.IsSystemRole)
            {
                if (!model.Active)
                {
                    ModelState.AddModelError(nameof(model.Active), T("Admin.Customers.CustomerRoles.Fields.Active.CantEditSystem"));
                }
                if (!role.SystemName.EqualsNoCase(model.SystemName))
                {
                    ModelState.AddModelError(nameof(model.SystemName), T("Admin.Customers.CustomerRoles.Fields.SystemName.CantEditSystem"));
                }
            }
        }
    }
}
