﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Dasync.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartstore.Admin.Models.Directory;
using Smartstore.ComponentModel;
using Smartstore.Core.Common;
using Smartstore.Core.Common.Services;
using Smartstore.Core.Data;
using Smartstore.Core.Localization;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;
using Smartstore.Web.Modelling;
using Smartstore.Web.Modelling.DataGrid;

namespace Smartstore.Admin.Controllers
{
    public class DeliveryTimeController : AdminController
    {
        private readonly SmartDbContext _db;
        private readonly IDeliveryTimeService _deliveryTimeService;
        private readonly ILocalizedEntityService _localizedEntityService;

        public DeliveryTimeController(SmartDbContext db, IDeliveryTimeService deliveryTimeService, ILocalizedEntityService localizedEntityService)
        {
            _db = db;
            _deliveryTimeService = deliveryTimeService;
            _localizedEntityService = localizedEntityService;
        }

        public IActionResult Index()
        {
            return RedirectToAction("List");
        }

        /// <summary>
        /// (AJAX) Gets a list of all available delivery times. 
        /// </summary>
        /// <param name="selectedIds">Ids of selected entities.</param>
        /// <returns>List of all delivery times as JSON.</returns>
        public async Task<IActionResult> AllDeliveryTimes(string selectedIds)
        {
            var deliveryTimes = await _db.DeliveryTimes
                .AsNoTracking()
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync();

            var selectedArr = selectedIds.ToIntArray();

            var data = deliveryTimes
                .Select(x => new ChoiceListItem
                {
                    Id = x.Id.ToString(),
                    Text = x.GetLocalized(y => y.Name).Value,
                    Description = _deliveryTimeService.GetFormattedDeliveryDate(x),
                    Selected = selectedArr.Contains(x.Id)
                })
                .ToList();

            return new JsonResult(data);
        }

        [Permission(Permissions.Configuration.DeliveryTime.Read)]
        public IActionResult List()
        {
            return View();
        }

        [HttpPost]
        [Permission(Permissions.Configuration.DeliveryTime.Read)]
        public async Task<IActionResult> DeliveryTimeList(GridCommand command)
        {
            var deliveryTimes = await _db.DeliveryTimes
                .AsNoTracking()
                .ApplyGridCommand(command)
                .ToPagedList(command)
                .LoadAsync();

            var deliveryTimeModels = await deliveryTimes
                .SelectAsync(async x =>
                {
                    var model = await MapperFactory.MapAsync<DeliveryTime, DeliveryTimeModel>(x);
                    model.DeliveryInfo = _deliveryTimeService.GetFormattedDeliveryDate(x);
                    return model;
                })
                .AsyncToList();

            var gridModel = new GridModel<DeliveryTimeModel>
            {
                Rows = deliveryTimeModels,
                Total = await deliveryTimes.GetTotalCountAsync()
            };

            return Json(gridModel);
        }

        [Permission(Permissions.Configuration.DeliveryTime.Create)]
        public IActionResult CreateDeliveryTimePopup(string btnId, string formId)
        {
            var model = new DeliveryTimeModel();
            AddLocales(model.Locales);

            ViewBag.BtnId = btnId;
            ViewBag.FormId = formId;

            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Configuration.DeliveryTime.Create)]
        public async Task<IActionResult> CreateDeliveryTimePopup(DeliveryTimeModel model, string btnId, string formId)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var deliveryTime = await MapperFactory.MapAsync<DeliveryTimeModel, DeliveryTime>(model);
                    _db.DeliveryTimes.Add(deliveryTime);
                    await _db.SaveChangesAsync();

                    await UpdateLocalesAsync(deliveryTime, model);

                    NotifySuccess(T("Admin.Configuration.DeliveryTime.Added"));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", ex.Message);
                    return View(model);
                }

                ViewBag.RefreshPage = true;
                ViewBag.BtnId = btnId;
                ViewBag.FormId = formId;
            }

            return View(model);
        }

        [Permission(Permissions.Configuration.DeliveryTime.Read)]
        public async Task<IActionResult> EditDeliveryTimePopup(int id, string btnId, string formId)
        {
            var deliveryTime = await _db.DeliveryTimes.FindByIdAsync(id, false);
            if (deliveryTime == null)
            {
                return NotFound();
            }

            var model = await MapperFactory.MapAsync<DeliveryTime, DeliveryTimeModel>(deliveryTime);

            AddLocales(model.Locales, (locale, languageId) =>
            {
                locale.Name = deliveryTime.GetLocalized(x => x.Name, languageId, false, false);
            });

            ViewBag.BtnId = btnId;
            ViewBag.FormId = formId;

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Permission(Permissions.Configuration.DeliveryTime.Update)]
        public async Task<IActionResult> EditDeliveryTimePopup(DeliveryTimeModel model, string btnId, string formId)
        {
            var deliveryTime = await _db.DeliveryTimes.FindByIdAsync(model.Id);
            if (deliveryTime == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    await MapperFactory.MapAsync(model, deliveryTime);
                    await UpdateLocalesAsync(deliveryTime, model);
                    await _db.SaveChangesAsync();

                    NotifySuccess(T("Admin.Configuration.DeliveryTimes.Updated"));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                    return View(model);
                }

                ViewBag.RefreshPage = true;
                ViewBag.BtnId = btnId;
                ViewBag.FormId = formId;
            }

            return View(model);
        }

        [HttpPost]
        [Permission(Permissions.Configuration.Measure.Update)]
        public async Task<IActionResult> SetDefault(int id)
        {
            Guard.NotZero(id, nameof(id));

            var deliveryTime = await _db.DeliveryTimes.FindByIdAsync(id);
            deliveryTime.IsDefault = true;
            await _db.SaveChangesAsync();

            return Json(new { Success = true });
        }

        [HttpPost]
        [Permission(Permissions.Configuration.DeliveryTime.Delete)]
        public async Task<IActionResult> Delete(GridSelection selection)
        {
            var success = false;
            var numDeleted = 0;
            var ids = selection.GetEntityIds();

            if (ids.Any())
            {
                var deliveryTimes = await _db.DeliveryTimes.GetManyAsync(ids, true);
                var triedToDeleteDefault = false;

                foreach (var deliveryTime in deliveryTimes)
                {
                    if (deliveryTime.IsDefault == true)
                    {
                        // INFO: The hook only handles setting all other deleivery times to not default.
                        // But doesnt not prevent the default delivery time from being deleted nor informs the user to do the correct thing.
                        // TODO: (mh) (core) Remove comments after review.
                        triedToDeleteDefault = true;
                        NotifyError(T("Admin.Configuration.DeliveryTimes.CantDeleteDefault"));
                    }
                    else
                    {
                        _db.DeliveryTimes.Remove(deliveryTime);
                    }
                }

                numDeleted = await _db.SaveChangesAsync();

                if (triedToDeleteDefault && numDeleted == 0)
                {
                    success = false;
                }
                else
                {
                    success = true;
                }
            }

            return Json(new { Success = success, Count = numDeleted });
        }

        [NonAction]
        private async Task UpdateLocalesAsync(DeliveryTime deliveryTime, DeliveryTimeModel model)
        {
            foreach (var localized in model.Locales)
            {
                await _localizedEntityService.ApplyLocalizedValueAsync(deliveryTime, x => x.Name, localized.Name, localized.LanguageId);
            }
        }
    }
}