<#import "template.ftl" as layout>
<@layout.emailLayout>
<p data-dp="intro">${msg("dovepeakEmailVerificationIntro", realmName)}</p>
<@layout.button href=link label=msg("dovepeakEmailVerificationAction") />
<p>${msg("dovepeakLinkExpiry", linkExpirationFormatter(linkExpiration))}</p>
<p style="color:#5b5b78;">${msg("dovepeakEmailVerificationIgnore")}</p>
</@layout.emailLayout>
