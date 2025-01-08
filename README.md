
# Solis Manager - Automated Battery Management for Solis Inverters and Octopus Agile

This app is designed to optimally manage the battery charging for your Solar/PV/Battery system, when
used with the Octopus Agile tariff. The idea is that it will analyse upcoming Agile prices, and then
pick a strategy to charge your battery based on the cheapest periods.

<img width="1672" alt="SolisManagerScreenshot" src="https://github.com/user-attachments/assets/926fb797-c8b5-4b13-bd8d-6a386954f794" />

### Warranty / Disclaimer 

**PLEASE NOTE:** This application is provided as-is, with no guarantees; I accept no liability for any issues that arise with 
your electricity bill, inverter, battery, or any other system, as a result of using this application. **Your
choice to run the app is entirely at your own risk. **

## Installation

SolisManager runs as a server-based app, and is designed to run 24/7, in the background, with minimal 
interaction from the user. If it's working well, you shouldn't have to do anything once it's set up.

To run it, download and unpack the binary package from Github releases, and then run the main executable.

Once the server is running, navigate to the UI via your browser. It will be a `{ip/hostname of server}:5169`.

## Settings

The first time you load the UI, you'll be prompted to input basic information, such as your Solis API key and 
secret, your inverter serial number, and the Octopus Product details of the current tariff you're on. Note 
that for this to work, you'll need to have raised a ticket with Solis to get access to control the inverter
via the SolisCloud app. 

<img width="630" alt="SolisManagerSettings" src="https://github.com/user-attachments/assets/a1c40d71-2d46-4222-bdd5-e4c1d298b01d" />

You'll also need to set some other config setting:

* Max Charge Rate in Amps - set to the level that your battery can charge/discharge at.
* Charge slots for full battery - which tells the app how many slots of charging will be needed to go from
  empty to full. This will depend on your battery size and charging rate.
* Low Battery Threshold - the percentage at which you'd like to eagerly charge if prices are a bit lower
  than average
* The `Always charge below` rate. For example, if you set this to 10p/kWh, then _any_ slot lower than that
  price will always be set to charge, regardless of anything else.
* Simulate-only - if checked, the app will run and simulate what it _would have done_ without actually making
  any changes to the behaviour.
  
Once you've filled these in, the server will start running.

As it runs, the charging decisions will be logged to `SolisManagerExecutionHistory.csv` so you can monitor the 
decisions it's taking to ensure they're as you require.
  
### How does it work?

At first launch SolisManager will load the next set of Agile Tariff data, along with some information about
your inverter. It will then estimate the best charging strategy based on a number of rules, as set out below. 
Note that this strategy is based on my needs for battery-management, but should apply to many other people too.

### Who Will It Work For?

This application is based on a number of assumptions, the primary one of which is that the person running it 
is a high-consumption power user, probably with an Air Source Heat Pump or EV, and wants to optimise their 
battery charging to charge at the cheapest times. The goal of the app is to charge the battery at the cheapest
times possible, without too much unnecessary battery cycling. If you are a lower-useage household, you may find
that Rob Tweed's [Agility](https://github.com/robtweed/agility) app is better suited to your needs.

### The Algorithm/Strategy

* First, find the cheapest period for charging the battery. This is the set of contiguous
  slots, long enough when combined that they can charge the battery from empty to full, and
  that has the cheapest average price for that period. This will typically be around 1am in 
  the morning, but can shift around a bit.
* Then perform a similar calculation to find the most expensive (peak) period.
* Set the action for all cheapest slots to `Charge`
* Set the action for the most expensive slots to `Do Nothing` - i.e., don't charge.
* Now, we've calculated the cheapest and most expensive slots. From the remaining slots, calculate
  the average rate across them. We then use that average rate to determine if any other slots across
  the day are a bit cheaper. So look for anything that's 90% of the average or less, and mark it
  as `BelowAverage`. For those slots, if the battery is low, we'll take the opportunity to charge as 
  they're a bit cheaper-than-average, so set their action to `Charge If Low Battery`.
* If we have a set of cheapest slots, then the price will usually start to drop a few slots before
  it's actually cheapest; these will likely be slots that are BelowAverage pricing in the run-up to
  the cheapest period. However, we don't want to charge then, because otherwise by the time we
  get to the cheapest period, the battery will be full. So back up `n` slots and even if they're
  `BelowAverage`, set them to `Do Nothing`.
* If we have a set of priciest slots, we want to ensure the battery is charged before we reach them.
  So work backwards from the first expensive slot, applying a charge instruction on each slot before
  the priciest, until we've got enough slots to fully charge the battery.
  Note that we skip an extra one, because the slot before the priciest ones is almost always a bit more
  expensive too. Just in case it's REALLY expensive, allow ourselves a couple of steps back to look
  for slightly cheaper rates. Can't go back too far though otherwise the battery might not last through
  the peak period.
* If there are any slots below our "_Blimey it's cheap_" threshold, elect to charge them anyway. E.g.,
  we may elect to always charge if the price is negative, or if it's below our export rate.
* For any slots that are set to `Charge If Low Battery`, update them to 'charge' if the battery SOC is,
  indeed, low. Only do this for enough slots to fully charge the battery.

There are also _manual overrides_ which can be set vie the tools screen. For example:

* Charge the Battery - this will override the next `n` slots, regardless of cost, to completely
  charge the battery
* Discharge the Battery - this will override the next `n` slots to completely discharge the battery

### Coming Soon:

* The intention is to provide pre-built docker images for ARM64 (RasPi) and other X64 Linus platforms. I run
  this on my Synology NAS.
* A future feature enhancement is to allow the app to determine if Octopus prices fall below zero for a certain
  period, and if so to automatically dump the battery charge to the grid, and then recharge the battery.
* The app will read in Solcast forecast data if you provide an API key and Site ID. Currently this information
  is only used for display purposes, but will eventually be used to optimise the algorithm (e.g., by skipping
  overnight charging if the forecast is for a decent PV yield.

### Thanks/Credits

* Thanks must go to [Rob Tweed](https://github.com/robtweed) whose Agility project was the inspiration for this
  application. 
* Thanks also to [Jon Glass](https://github.com/jmg48/solis-cloud) for his sample .Net wrapper for the Solis API,
  without which I'd have spent an inordinate amount of time figuring out the complicated Solis Authentication
  process.

### Technical Details

For those who are interested, the application is built using Blazor WebAssembly, with an ASP.Net back-end. The
app is written entirely in C#, using .Net 9. It was written over the course of about 5 days. 
