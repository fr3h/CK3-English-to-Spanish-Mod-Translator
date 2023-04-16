import sys
import requests
from argostranslate import package, translate

def download_language_package(url):
    response = requests.get(url)
    response.raise_for_status()

def check_and_download_language_package(from_code, to_code):
    package.update_package_index()
    installed_packages = package.get_installed_packages()
    installed_languages = next(
        filter(
            lambda x: x.from_code == from_code and x.to_code == to_code, installed_packages
        ),
        None
    )

    if not installed_languages:
        print(f"{from_code}-{to_code} package not installed. Downloading...")
        available_packages = package.get_available_packages()
        package_to_install = next(
            filter(
                lambda x: x.from_code == from_code and x.to_code == to_code, available_packages
            ),
            None
        )
        if package_to_install:
            package.install_from_path(package_to_install.download())
            print(f"{from_code}-{to_code} package installed successfully.")
        else:
            print(f"No se encontró el paquete {from_code}-{to_code}.")

def main():
    text = sys.argv[1]
    from_lang = sys.argv[2]
    to_lang = sys.argv[3]

    check_and_download_language_package(from_lang, to_lang)

    translated_text = translate.translate(text, from_lang, to_lang)

    print(translated_text, end="")

if __name__ == "__main__":
    main()
