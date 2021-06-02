## Updating the upstream/aspnetcore/web.js directory

The contents of this directory come from https://github.com/aspnet/AspNetCore repo. I didn't want to use a real git submodule because that's such a giant repo, and I only need a few files from it here. So instead I used the `git read-tree` technique described at https://stackoverflow.com/a/30386041

One-time setup per working copy:

    git remote add -t main --no-tags aspnetcore https://github.com/aspnet/AspNetCore.git

Then, to update the contents of upstream/aspnetcore/web.js to the latest:

    cd <directory containing this .md file>
    git rm -rf upstream/aspnetcore
    git fetch --depth 1 aspnetcore
    git read-tree --prefix=Photino.Blazor.JS/upstream/aspnetcore/web.js -u aspnetcore/main:src/Components/Web.JS
    git read-tree --prefix=RemotePhotino.Blazor.JS/upstream/aspnetcore/web.js -u v6.0.0-preview.3.21201.13:src/Components/Web.JS
    git commit -m "Get Web.JS files from commit v6.0.0-preview.3.21201.13"

    When using these commands, replace:

    * `main` with the branch you want to fetch from
    * `v6.0.0-preview.3.21201.13` with the TAG of the commit you're fetching from

    Longer term, we may consider publishing Components.Web.JS as a NuGet package
    with embedded .ts sources, so that it's possible to use inside a WebPack build
    without needing to clone its sources.


    Next:

    cd upstream\aspnetcore\web.js
    yarn


