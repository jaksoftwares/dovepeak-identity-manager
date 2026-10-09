<#import "template.ftl" as layout>
<@layout.emailLayout>
<p data-dp="intro">${msg("dovepeakPasswordResetIntro", realmName)}</p>
<@layout.button href=link label=msg("dovepeakPasswordResetAction") />
<p>${msg("dovepeakLinkExpiry", linkExpirationFormatter(linkExpiration))}</p>
<p style="color:#5b5b78;">${msg("dovepeakPasswordResetIgnore")}</p>
</@layout.emailLayout>
